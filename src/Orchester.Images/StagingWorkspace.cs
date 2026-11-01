using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Orchester.Core;
using Orchester.Security;
namespace Orchester.Images;

// Prepares an inspectable directory bundle. It does not create a partition or modify an ESP.
public sealed class StagingWorkspace {
    public async Task<StagingReceipt> PrepareAsync(InstallationPlan plan, ReleaseProfile profile,
        InstallerRecipe recipe, string parent, bool fat32, CancellationToken ct) {
        if (recipe.ProfileId != plan.ProfileId || profile.Id != plan.ProfileId || plan.Image.ProfileId != plan.ProfileId)
            throw new SafetyException("AdapterMismatch");
        if (recipe.RequiresAdditionalHdMedia) throw new SafetyException("HdMediaRequired");
        if (fat32 && plan.Image.Length >= uint.MaxValue) throw new SafetyException("Fat32FileLimit");
        WorkspaceSafety.RejectReparsePoints(parent); Directory.CreateDirectory(parent);
        string target = WorkspaceSafety.ResolveChild(parent, plan.OperationId.ToString("D"));
        if (Directory.Exists(target) || File.Exists(target)) throw new SafetyException("DestinationExists");
        await ImageVerifier.VerifyAsync(plan.Image.Path, plan.Image.Sha256, plan.ProfileId, plan.Image.Trust, ct);
        await using var iso = await Iso9660Reader.OpenAsync(plan.Image.Path, ct);
        if(new FileInfo(plan.Image.Path).Length!=plan.Image.Length)throw new SafetyException("ImageMismatch");
        await iso.VerifySourceAsync(plan.Image.Sha256,ct);
        var kernel = await iso.FindAsync(profile.KernelPath, ct);
        var initrd = await iso.FindAsync(profile.InitrdPath, ct);
        var efi = await iso.FindAsync("/EFI/BOOT/BOOTX64.EFI", ct);
        long total = checked(plan.Image.Length + kernel.Length + initrd.Length + efi.Length + 16 * 1024 * 1024);
        if (total > plan.StagingBytes) throw new SafetyException("InsufficientStagingSpace");
        Directory.CreateDirectory(target); WorkspaceSafety.MakePrivate(target, true);
        // A marker is created exclusively, before data. An interrupted directory is never silently reused.
        await using (var marker = new FileStream(Path.Combine(target, "preparing"), FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
            await marker.WriteAsync(plan.OperationId.ToByteArray(), ct); marker.Flush(true);
        }
        var files = ImmutableArray.CreateBuilder<StagedFile>();
        async Task Extract(IsoFile file, string relative) {
            string output = WorkspaceSafety.ResolveChild(target, relative); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            files.Add(await iso.ExtractAsync(file, output, relative, ct));
        }
        await Extract(kernel, "orchester/vmlinuz"); await Extract(initrd, "orchester/initrd");
        // This is an inspected component, not a certified boot chain. No firmware registration happens here.
        await Extract(efi, "orchester/inspected-bootx64.efi");
        string source = WorkspaceSafety.ResolveChild(target, "orchester/source.iso");
        using (var input = new FileStream(plan.Image.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous))
        using (var output = new FileStream(source, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough)) {
            await input.CopyToAsync(output, 1024 * 1024, ct); await output.FlushAsync(ct); output.Flush(true);
        }
        var copied = await ImageVerifier.VerifyAsync(source, plan.Image.Sha256, plan.ProfileId, plan.Image.Trust, ct);
        files.Add(new("orchester/source.iso", copied.Length, copied.Sha256));
        var receipt = new StagingReceipt(1, plan.OperationId, PlanGuard.Fingerprint(plan), plan.Image.Sha256,
            profile.Id, files.ToImmutable(), DateTimeOffset.UtcNow);
        string receiptPath = Path.Combine(target, "staging.json");
        using (var file = new FileStream(receiptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) {
            await JsonSerializer.SerializeAsync(file, receipt, cancellationToken: ct); file.Flush(true);
        }
        File.Delete(Path.Combine(target, "preparing"));
        return receipt;
    }
    public async Task VerifyAsync(string target, InstallationPlan plan, ReleaseProfile profile, CancellationToken ct) {
        WorkspaceSafety.RejectReparsePoints(target);
        if (File.Exists(Path.Combine(target, "preparing"))) throw new SafetyException("StagingIncomplete");
        var receipt = JsonSerializer.Deserialize<StagingReceipt>(await File.ReadAllTextAsync(Path.Combine(target, "staging.json"), ct))
            ?? throw new SafetyException("StagingIncomplete");
        if (receipt.SchemaVersion != 1 || receipt.OperationId != plan.OperationId ||
            receipt.PlanFingerprint != PlanGuard.Fingerprint(plan) || receipt.ImageSha256 != plan.Image.Sha256 ||
            receipt.Files.IsDefaultOrEmpty || receipt.Files.Select(f => f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != receipt.Files.Length)
            throw new SafetyException("StagingIncomplete");
        if(profile.Id!=plan.ProfileId||receipt.ProfileId!=profile.Id)throw new SafetyException("StagingIncomplete");
        await using var source=await Iso9660Reader.OpenAsync(plan.Image.Path,ct);
        await source.VerifySourceAsync(plan.Image.Sha256,ct);
        var sourceHashes=new Dictionary<string,string> {
            ["orchester/vmlinuz"]=await source.HashAsync(await source.FindAsync(profile.KernelPath,ct),ct),
            ["orchester/initrd"]=await source.HashAsync(await source.FindAsync(profile.InitrdPath,ct),ct),
            ["orchester/inspected-bootx64.efi"]=await source.HashAsync(await source.FindAsync("/EFI/BOOT/BOOTX64.EFI",ct),ct),
            ["orchester/source.iso"]=plan.Image.Sha256.ToLowerInvariant()
        };
        var allowed=sourceHashes.Keys.Append("staging.json").ToHashSet(StringComparer.Ordinal);
        foreach(var entry in Directory.EnumerateFileSystemEntries(target)) {
            WorkspaceSafety.RejectReparsePoints(entry);
            if(Directory.Exists(entry)) {
                if(Path.GetFileName(entry)!="orchester")throw new SafetyException("StagingChanged");
                foreach(var child in Directory.EnumerateFileSystemEntries(entry)) {
                    WorkspaceSafety.RejectReparsePoints(child);
                    if(Directory.Exists(child)||!allowed.Contains("orchester/"+Path.GetFileName(child)))throw new SafetyException("StagingChanged");
                }
            } else if(!allowed.Contains(Path.GetFileName(entry)))throw new SafetyException("StagingChanged");
        }
        if(receipt.Files.Length!=sourceHashes.Count)throw new SafetyException("StagingIncomplete");
        foreach (var expected in receipt.Files) {
            if(!sourceHashes.TryGetValue(expected.RelativePath,out var authenticHash)||!authenticHash.Equals(expected.Sha256,StringComparison.OrdinalIgnoreCase))throw new SafetyException("StagingChanged");
            string path = WorkspaceSafety.ResolveChild(target, expected.RelativePath);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length != expected.Length || !Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new SafetyException("StagingChanged");
        }
    }
}
