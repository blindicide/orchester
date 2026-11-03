using System.Collections.Immutable;
using System.Text.Json;
using Orchester.Core;
using Orchester.Security;
namespace Orchester.Engine;

public interface IExecutionBackend {
    Task PreflightAsync(InstallationPlan plan, CancellationToken ct);
    Task<ExecutionArtifact> PrepareStorageAsync(InstallationPlan plan, CancellationToken ct);
    Task<ExecutionArtifact> StageInstallerAsync(InstallationPlan plan, CancellationToken ct);
    Task<ExecutionArtifact> ConfigureBootAsync(InstallationPlan plan, CancellationToken ct);
    Task RequestOneTimeBootAsync(InstallationPlan plan, CancellationToken ct);
}
public sealed record ExecutionArtifact(Guid ResourceId, string Sha256);
public sealed record ExecutionRecord(long Sequence, Guid OperationId, OperationPhase Phase, string Action,
    string Status, DateTimeOffset Timestamp, string PlanFingerprint, ExecutionArtifact? Receipt);

// Append-only write-ahead log: a critical intent is flushed before invoking its backend.
// Recovery never retries an interrupted storage/firmware action without independent inspection.
public sealed class ExecutionCoordinator(string root, IExecutionBackend? backend) {
    public async Task<OperationPhase> PrepareAsync(InstallationPlan plan, string approvedFingerprint, CancellationToken ct) {
        if (plan.DryRun) throw new SafetyException("ExecutionUnavailable");
        if (PlanGuard.Fingerprint(plan) != approvedFingerprint) throw new SafetyException("PlanChanged");
        string folder = Folder(plan.OperationId); Directory.CreateDirectory(folder);
        using var lease = new FileStream(Path.Combine(folder, "execution.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(Path.Combine(folder, "execution.jsonl")) || File.Exists(Path.Combine(folder, "plan.json"))) throw new SafetyException("RecoveryInspectionRequired");
        using (var saved = new FileStream(Path.Combine(folder, "plan.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough)) {
            await JsonSerializer.SerializeAsync(saved, plan, cancellationToken: ct); saved.Flush(true);
        }
        long sequence = 0; OperationPhase phase = OperationPhase.PLAN_APPROVED; bool criticalStarted = false;
        async Task Write(string action, string status, ExecutionArtifact? receipt=null) {
            if (receipt is not null && (receipt.ResourceId == Guid.Empty || receipt.Sha256.Length != 64 || !receipt.Sha256.All(Uri.IsHexDigit)))
                throw new SafetyException("InvalidExecutionReceipt");
            var entry = new ExecutionRecord(++sequence, plan.OperationId, phase, action, status, DateTimeOffset.UtcNow, approvedFingerprint, receipt);
            using var file = new FileStream(Path.Combine(folder, "execution.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            await JsonSerializer.SerializeAsync(file, entry, cancellationToken: CancellationToken.None);
            await file.WriteAsync(new byte[]{10}, CancellationToken.None); file.Flush(true);
        }
        try {
            await Write("Preflight", "INTENT"); await backend!.PreflightAsync(plan, ct); await Write("Preflight", "VERIFIED");
            ct.ThrowIfCancellationRequested();
            await Write("PrepareStorage", "INTENT"); criticalStarted = true;
            ExecutionArtifact storage = await backend!.PrepareStorageAsync(plan, CancellationToken.None);
            phase = OperationPhase.STORAGE_PREPARED; await Write("PrepareStorage", "VERIFIED", storage);
            await Write("StageInstaller", "INTENT");
            ExecutionArtifact staging = await backend!.StageInstallerAsync(plan, ct);
            phase = OperationPhase.INSTALLER_STAGED; await Write("StageInstaller", "VERIFIED", staging);
            await Write("ConfigureBoot", "INTENT");
            ExecutionArtifact boot = await backend!.ConfigureBootAsync(plan, CancellationToken.None);
            phase = OperationPhase.BOOT_CONFIGURED; await Write("ConfigureBoot", "VERIFIED", boot);
            return phase; // Reboot is a separate explicit action.
        } catch (OperationCanceledException) {
            phase = criticalStarted ? OperationPhase.RECOVERY_REQUIRED : OperationPhase.CANCELLED;
            await Write("Cancellation", "STOPPED"); throw;
        } catch {
            phase = criticalStarted ? OperationPhase.RECOVERY_REQUIRED : OperationPhase.FAILED;
            await Write("Failure", "STOPPED"); throw;
        }
    }
    public async Task RequestBootAsync(InstallationPlan plan, CancellationToken ct) {
        string folder = Folder(plan.OperationId);
        using var lease = new FileStream(Path.Combine(folder, "execution.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var records = await ReadAsync(plan.OperationId, ct);
        if (records.IsEmpty || records[^1].Phase != OperationPhase.BOOT_CONFIGURED || records[^1].Status != "VERIFIED" ||
            records[^1].PlanFingerprint != PlanGuard.Fingerprint(plan)) throw new SafetyException("InvalidTransition");
        async Task Append(string status, OperationPhase phase) {
            using var file = new FileStream(Path.Combine(folder, "execution.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            var entry = new ExecutionRecord(records.Length + (status == "INTENT" ? 1 : 2), plan.OperationId, phase, "RequestBoot", status, DateTimeOffset.UtcNow, PlanGuard.Fingerprint(plan), null);
            await JsonSerializer.SerializeAsync(file, entry, cancellationToken: CancellationToken.None); await file.WriteAsync(new byte[]{10}); file.Flush(true);
        }
        ct.ThrowIfCancellationRequested(); await Append("INTENT", OperationPhase.BOOT_CONFIGURED);
        try { await backend!.RequestOneTimeBootAsync(plan, CancellationToken.None); await Append("VERIFIED", OperationPhase.REBOOT_REQUESTED); }
        catch { await Append("STOPPED", OperationPhase.UNKNOWN_EXTERNAL_STATE); throw; }
    }
    public async Task<ImmutableArray<ExecutionRecord>> ReadAsync(Guid id, CancellationToken ct) {
        var entries = ImmutableArray.CreateBuilder<ExecutionRecord>(); string? fingerprint = null;
        try {
            foreach (string line in await File.ReadAllLinesAsync(Path.Combine(Folder(id), "execution.jsonl"), ct)) {
                var entry = JsonSerializer.Deserialize<ExecutionRecord>(line) ?? throw new SafetyException("JournalIncomplete");
                fingerprint ??= entry.PlanFingerprint;
                if (entry.OperationId != id || entry.Sequence != entries.Count + 1 || entry.PlanFingerprint != fingerprint || entry.Status is not ("INTENT" or "VERIFIED" or "STOPPED"))
                    throw new SafetyException("JournalIncomplete");
                entries.Add(entry);
            }
        } catch (JsonException) { throw new SafetyException("JournalIncomplete"); }
        var savedPlan = JsonSerializer.Deserialize<InstallationPlan>(await File.ReadAllTextAsync(Path.Combine(Folder(id), "plan.json"), ct)) ?? throw new SafetyException("JournalIncomplete");
        if (savedPlan.OperationId != id || (fingerprint is not null && PlanGuard.Fingerprint(savedPlan) != fingerprint)) throw new SafetyException("JournalIncomplete");
        string[] actions=["Preflight","PrepareStorage","StageInstaller","ConfigureBoot","RequestBoot"];
        OperationPhase[] phases=[OperationPhase.PLAN_APPROVED,OperationPhase.STORAGE_PREPARED,OperationPhase.INSTALLER_STAGED,OperationPhase.BOOT_CONFIGURED,OperationPhase.REBOOT_REQUESTED];
        int step=0;bool pending=false;OperationPhase prior=OperationPhase.PLAN_APPROVED;
        for (int i = 0; i < entries.Count; i++) {
            var entry=entries[i];
            if(!Enum.IsDefined(entry.Phase))throw new SafetyException("JournalIncomplete");
            if(entry.Status=="STOPPED") {
                if(i!=entries.Count-1 || entry.Phase is not (OperationPhase.FAILED or OperationPhase.CANCELLED or OperationPhase.RECOVERY_REQUIRED or OperationPhase.UNKNOWN_EXTERNAL_STATE))throw new SafetyException("JournalIncomplete");
                continue;
            }
            if(step>=actions.Length||entry.Action!=actions[step])throw new SafetyException("JournalIncomplete");
            if(entry.Status=="INTENT") {
                if(pending||entry.Phase!=prior||entry.Receipt is not null)throw new SafetyException("JournalIncomplete");
                pending=true;
            } else {
                if(!pending||entry.Phase!=phases[step])throw new SafetyException("JournalIncomplete");
                if(step is 1 or 2 or 3 && (entry.Receipt is null||entry.Receipt.ResourceId==Guid.Empty||entry.Receipt.Sha256.Length!=64||!entry.Receipt.Sha256.All(Uri.IsHexDigit)))throw new SafetyException("JournalIncomplete");
                pending=false;prior=entry.Phase;step++;
            }
        }
        return entries.ToImmutable();
    }
    public async Task<OperationPhase> InspectRecoveryAsync(Guid id, CancellationToken ct) {
        try {
            var records = await ReadAsync(id, ct);
            if (records.IsEmpty || records[^1].Status == "INTENT") return OperationPhase.RECOVERY_REQUIRED;
            return records[^1].Phase;
        } catch (Exception e) when (e is IOException or SafetyException or JsonException) { return OperationPhase.RECOVERY_REQUIRED; }
    }
    private string Folder(Guid id) {
        if (id == Guid.Empty) throw new SafetyException("UnauthorizedRequest");
        return WorkspaceSafety.ResolveChild(root, id.ToString("D"));
    }
}
