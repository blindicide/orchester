using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using Orchester.Core;
namespace Orchester.Security;

public static class WorkspaceSafety {
    public static string ResolveChild(string root, string relative) {
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Contains('\\') ||
            relative.Split('/').Any(p => p is "" or "." or "..")) throw new SafetyException("UnsafePath");
        root = Path.GetFullPath(root);
        string result = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new SafetyException("UnsafePath");
        RejectReparsePoints(result);
        return result;
    }
    public static void RejectReparsePoints(string path) {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current)) {
            try {if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new SafetyException("UnsafePath");}
            catch(FileNotFoundException) { }
            catch(DirectoryNotFoundException) { }
        }
    }
    public static void MakePrivate(string path, bool directory) {
        if (OperatingSystem.IsWindows()) MakePrivateWindows(path, directory);
        else File.SetUnixFileMode(path, directory ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute : UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    [SupportedOSPlatform("windows")]
    private static void MakePrivateWindows(string path, bool directory) {
        var user = WindowsIdentity.GetCurrent().User ?? throw new SafetyException("SecretStorageUnavailable");
        if (directory) {
            var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(user);
            acl.AddAccessRule(new(user, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(acl);
        } else {
            var acl = new FileSecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(user);
            acl.AddAccessRule(new(user, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(acl);
        }
    }
}

public sealed class SecretWorkspace : IAsyncDisposable {
    public string DirectoryPath { get; }
    public SecretWorkspace(string parent, Guid operationId) {
        WorkspaceSafety.RejectReparsePoints(parent); Directory.CreateDirectory(parent);
        DirectoryPath = WorkspaceSafety.ResolveChild(parent, operationId.ToString("D") + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        Directory.CreateDirectory(DirectoryPath); WorkspaceSafety.MakePrivate(DirectoryPath, true);
    }
    public async Task<string> WriteAsync(string name, ReadOnlyMemory<byte> bytes, CancellationToken ct) {
        string path = WorkspaceSafety.ResolveChild(DirectoryPath, name);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous);
        WorkspaceSafety.MakePrivate(path, false); // Apply restrictive access before writing any secret bytes.
        await file.WriteAsync(bytes, ct); await file.FlushAsync(ct); file.Flush(true);
        return path;
    }
    public ValueTask DisposeAsync() {
        WorkspaceSafety.RejectReparsePoints(DirectoryPath);
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
        // Logical deletion; no claim of secure erasure on SSDs or journaled filesystems.
        return ValueTask.CompletedTask;
    }
}

public static class ReleaseGate {
    private sealed record Lease(Guid OperationId,DateTimeOffset Expires);
    private static readonly AsyncLocal<Lease?> Current=new();
    public static IDisposable Authorize(Guid operationId) {
        if(!OperatingSystem.IsWindows()||operationId==Guid.Empty)throw new SafetyException("UnauthorizedRequest");
        if(!IsAdministrator())throw new SafetyException("UnauthorizedRequest");
        var old=Current.Value;Current.Value=new(operationId,DateTimeOffset.UtcNow.AddHours(3));return new Revert(()=>Current.Value=old);
    }
    [SupportedOSPlatform("windows")]
    private static bool IsAdministrator()=>new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public static void RequireNativeWrites()=>RequireNativeWrites(Current.Value?.OperationId??Guid.Empty);
    public static void RequireNativeWrites(Guid operationId) {
        if(Current.Value is not { } lease||operationId==Guid.Empty||lease.OperationId!=operationId||lease.Expires<DateTimeOffset.UtcNow)
            throw new SafetyException("UnauthorizedRequest");
    }
    private sealed class Revert(Action restore):IDisposable {public void Dispose()=>restore();}
}
