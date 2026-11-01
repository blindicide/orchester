using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Orchester.Boot;
using Orchester.Core;
using Orchester.Security;
namespace Orchester.Engine;

[SupportedOSPlatform("windows")]
public static class WindowsBootBackup {
    public static async Task SaveAsync(string folder,CancellationToken ct) {
        await DurableWriteAsync(Path.Combine(folder,"host-before.json"),JsonSerializer.Serialize(HostInspector.Inspect()),ct);
        WindowsFirmwareVariables.EnableReadPrivilege();var variables=new WindowsFirmwareVariables();
        var snapshot=new UefiBootService(variables).Inspect();
        await DurableWriteAsync(Path.Combine(folder,"firmware-before.json"),JsonSerializer.Serialize(snapshot),ct);
        var raw=new Dictionary<string,byte[]>();
        foreach(var entry in snapshot.Entries.Where(e=>e.WindowsBootManager)) {
            string name="Boot"+entry.Number.ToString("X4");raw[name]=variables.Read(name)??throw new SafetyException("WindowsBootMissing");
        }
        await DurableWriteAsync(Path.Combine(folder,"windows-entries.json"),JsonSerializer.Serialize(raw),ct);
        await BcdAsync("/export",Path.Combine(folder,"windows-before.bcd"),ct);
        using var file=new FileStream(Path.Combine(folder,"windows-before.bcd"),FileMode.Open,FileAccess.Read,FileShare.Read);
        await DurableWriteAsync(Path.Combine(folder,"windows-before.sha256"),Convert.ToHexString(await SHA256.HashDataAsync(file,ct)),ct);
    }
    public static async Task RestoreAsync(string folder,CancellationToken ct) {
        var host=JsonSerializer.Deserialize<HostSnapshot>(await File.ReadAllTextAsync(Path.Combine(folder,"host-before.json"),ct))??throw new SafetyException("JournalIncomplete");
        if(host.WindowsVersion!=HostInspector.Inspect().WindowsVersion)throw new SafetyException("BootBackupIncompatible");
        string backup=Path.Combine(folder,"windows-before.bcd");
        using(var file=new FileStream(backup,FileMode.Open,FileAccess.Read,FileShare.Read)) {
            string expected=await File.ReadAllTextAsync(Path.Combine(folder,"windows-before.sha256"),ct);
            if(Convert.ToHexString(await SHA256.HashDataAsync(file,ct))!=expected)throw new SafetyException("StagingChanged");
        }
        // Validate every firmware restore target before changing the current BCD.
        WindowsFirmwareVariables.EnableReadPrivilege();var variables=new WindowsFirmwareVariables();
        var originals=JsonSerializer.Deserialize<Dictionary<string,byte[]>>(await File.ReadAllTextAsync(Path.Combine(folder,"windows-entries.json"),ct))??throw new SafetyException("JournalIncomplete");
        foreach(var entry in originals) {
            if(!System.Text.RegularExpressions.Regex.IsMatch(entry.Key,@"\ABoot[0-9A-F]{4}\z")||!UefiLoadOption.Parse(Convert.ToUInt16(entry.Key[4..],16),entry.Value).WindowsBootManager)throw new SafetyException("BootOwnershipMismatch");
            byte[]? existing=variables.Read(entry.Key);
            if(existing is not null&&!existing.SequenceEqual(entry.Value))throw new SafetyException("BootConfigurationChanged");
        }
        string current=Path.Combine(folder,"windows-before-restore-"+DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+".bcd");
        await BcdAsync("/export",current,ct);
        await BcdAsync("/import",backup,ct);
        foreach(var entry in originals) {
            if(!System.Text.RegularExpressions.Regex.IsMatch(entry.Key,@"\ABoot[0-9A-F]{4}\z"))throw new SafetyException("JournalIncomplete");
            var info=UefiLoadOption.Parse(Convert.ToUInt16(entry.Key[4..],16),entry.Value);
            if(!info.WindowsBootManager)throw new SafetyException("BootOwnershipMismatch");
            byte[]? existing=variables.Read(entry.Key);
            if(existing is null)variables.Write(entry.Key,entry.Value);
            else if(!existing.SequenceEqual(entry.Value))throw new SafetyException("BootConfigurationChanged");
        }
    }
    private static async Task DurableWriteAsync(string path,string content,CancellationToken ct) {
        using var file=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough);
        await file.WriteAsync(System.Text.Encoding.UTF8.GetBytes(content),ct);await file.FlushAsync(ct);file.Flush(true);
    }
    private static async Task BcdAsync(string action,string file,CancellationToken ct) {
        ct.ThrowIfCancellationRequested();WorkspaceSafety.RejectReparsePoints(file);
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","bcdedit.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(action);start.ArgumentList.Add(file);
        using var process=Process.Start(start)??throw new SafetyException("BootVerificationFailed");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync(CancellationToken.None);await output;await error;
        if(process.ExitCode!=0)throw new SafetyException("BootVerificationFailed");
        if(action=="/export")using(var saved=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.Read)){saved.Flush(true);}
    }
}
