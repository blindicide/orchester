using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Orchester.Core;
using Orchester.Security;
namespace Orchester.App;

public static class ElevatedDiagnostics {
    private static readonly object LaunchLock=new();
    public static async Task<DiagnosticResponse> InspectAsync(DiagnosticCommand command, CancellationToken ct,InstallationPlan? plan=null,string? fingerprint=null) {
        return await SendAsync(new(1,plan?.OperationId??Guid.NewGuid(),"",command,plan,fingerprint),ct);
    }
    public static async Task<DiagnosticResponse> SendAsync(DiagnosticRequest request,CancellationToken ct,IProgress<OperationProgress>? progress=null) {
        string helper = Path.Combine(AppContext.BaseDirectory, "Orchester.Helper.exe");
        if (!File.Exists(helper)) throw new SafetyException("HelperMissing");
        WorkspaceSafety.RejectReparsePoints(helper);
        string pipe = "Orchester-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        string nonce = HelperProtocol.NewNonce();
        var start = new ProcessStartInfo(helper) { UseShellExecute=true, Verb="runas", WindowStyle=ProcessWindowStyle.Hidden };
        // Validated fixed-shape arguments; no shell command or executable path is accepted from the UI.
        start.ArgumentList.Add("--pipe"); start.ArgumentList.Add(pipe); start.ArgumentList.Add("--nonce"); start.ArgumentList.Add(nonce);
        Process? process;
        try {
            lock(LaunchLock) {
                var removed=new Dictionary<string,string?>();
                foreach(System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables()) {
                    string name=(string)variable.Key;
                    if(new[]{"DOTNET_","COMPLUS_","COR_","CORECLR_"}.Any(prefix=>name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))){removed[name]=(string?)variable.Value;Environment.SetEnvironmentVariable(name,null);}
                }
                try {process=Process.Start(start);}finally{foreach(var variable in removed)Environment.SetEnvironmentVariable(variable.Key,variable.Value);}
            }
        } catch (Win32Exception) { throw new SafetyException("ElevationDeclined"); }
        using (process) {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromHours(3));
            if(process is null)throw new SafetyException("HelperMissing");
            return await HelperProtocol.ExchangeAsync(pipe, request with {Nonce=nonce}, timeout.Token, process.Id,progress);
        }
    }
}
