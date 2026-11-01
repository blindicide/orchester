using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using Orchester.Core;
namespace Orchester.Security;

public enum DiagnosticCommand { InspectHost, InspectFirmware, ValidatePlan, InspectStorage, PrepareInstallation, RequestInstallerBoot, ListOperations, CleanupOperation, CompleteInstallation, InspectOperation, RestoreWindowsBoot, ReclaimAllocation, ExportOperation }
public sealed record DiagnosticRequest(int Version, Guid OperationId, string Nonce, DiagnosticCommand Command,
    InstallationPlan? Plan, string? Fingerprint, ExecutionRequest? Execution=null,bool LinuxBootConfirmed=false,bool RecoveryConsent=false);
public sealed record DiagnosticResponse(int Version, Guid OperationId, bool Success, string? ErrorCode, JsonElement? Data,bool IsProgress=false);
public static class HelperProtocol {
    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    public const int MaximumFrameBytes = 256 * 1024;
    public static string NewNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static void Authenticate(DiagnosticRequest request, string expectedNonce) {
        if (request.Version != 1 || request.OperationId == Guid.Empty || !Enum.IsDefined(request.Command) ||
            request.Nonce is null || request.Nonce.Length != 64 || expectedNonce.Length != 64 ||
            !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(request.Nonce), System.Text.Encoding.ASCII.GetBytes(expectedNonce)))
            throw new SafetyException("UnauthorizedRequest");
        if(request.Command==DiagnosticCommand.ValidatePlan) {
            var p=request.Plan;
            if(p is null||p.OperationId!=request.OperationId||p.OriginalDisk is null||p.Image is null||
                p.OriginalDisk.Partitions.IsDefault||p.OriginalDisk.Partitions.Any(partition=>partition is null)||
                p.Changes.IsDefault||p.Changes.Any(change=>change is null)||
                p.Image.Sha256 is not {Length:64}||!p.Image.Sha256.All(Uri.IsHexDigit)||
                request.Fingerprint is not {Length:64}||!request.Fingerprint.All(Uri.IsHexDigit))throw new SafetyException("UnauthorizedRequest");
        }
    }
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct) {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaximumFrameBytes) throw new SafetyException("IpcFrameInvalid");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, ct); await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct) {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, ct);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 2 || length > MaximumFrameBytes) throw new SafetyException("IpcFrameInvalid");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, ct);
        try { return JsonSerializer.Deserialize<T>(bytes) ?? throw new SafetyException("IpcFrameInvalid"); }
        catch (JsonException) { throw new SafetyException("IpcFrameInvalid"); }
    }
    public static async Task<DiagnosticResponse> ExchangeAsync(string pipeName, DiagnosticRequest request, CancellationToken ct, int? expectedServerPid=null,IProgress<OperationProgress>? progress=null) {
        if (!System.Text.RegularExpressions.Regex.IsMatch(pipeName, @"\AOrchester-[A-F0-9]{32}\z")) throw new SafetyException("UnauthorizedRequest");
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, OperatingSystem.IsWindows() ? PipeOptions.Asynchronous : PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,System.Security.Principal.TokenImpersonationLevel.Impersonation);
        await pipe.ConnectAsync(15000, ct);
        if (OperatingSystem.IsWindows() && (expectedServerPid is null || !GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint serverPid) || serverPid != expectedServerPid.Value))
            throw new SafetyException("UnauthorizedRequest");
        await WriteAsync(pipe, request, ct);
        while(true) {
            var response=await ReadAsync<DiagnosticResponse>(pipe,ct);
            if(response.Version!=1||response.OperationId!=request.OperationId)throw new SafetyException("UnauthorizedRequest");
            if(!response.IsProgress)return response;
            var update=response.Data?.Deserialize<OperationProgress>();
            if(update is not null&&update.OperationId==request.OperationId)progress?.Report(update);
        }
    }
}
