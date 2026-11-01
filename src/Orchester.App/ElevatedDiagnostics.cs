using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Orchester.Core;
using Orchester.Security;
namespace Orchester.App;

public static class ElevatedDiagnostics {
    public static async Task<DiagnosticResponse> InspectAsync(DiagnosticCommand command, CancellationToken ct) {
        string helper = Path.Combine(AppContext.BaseDirectory, "Orchester.Helper.exe");
        if (!File.Exists(helper)) throw new SafetyException("HelperMissing");
        WorkspaceSafety.RejectReparsePoints(helper);
        string pipe = "Orchester-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        string nonce = HelperProtocol.NewNonce();
        var start = new ProcessStartInfo(helper) { UseShellExecute=true, Verb="runas", WindowStyle=ProcessWindowStyle.Hidden };
        // Validated fixed-shape arguments; no shell command or executable path is accepted from the UI.
        start.ArgumentList.Add("--pipe"); start.ArgumentList.Add(pipe); start.ArgumentList.Add("--nonce"); start.ArgumentList.Add(nonce);
        Process? process;
        try { process = Process.Start(start); } catch (Win32Exception) { throw new SafetyException("ElevationDeclined"); }
        using (process) {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(170));
            if(process is null)throw new SafetyException("HelperMissing");
            return await HelperProtocol.ExchangeAsync(pipe, new(1, Guid.NewGuid(), nonce, command, null, null), timeout.Token, process.Id);
        }
    }
}
