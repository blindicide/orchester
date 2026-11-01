using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orchester.Boot;
using Orchester.Core;
using Orchester.Security;
using Orchester.Storage;

if(args.Length==1&&args[0]=="--can-uninstall") {
 try {
  var operations=new Orchester.Engine.PrivilegedOperationStore();
  if(!await operations.CanUninstallAsync(CancellationToken.None))return 3;
  return 0;
 }catch{return 3;}
}
if (args.Length is not (4 or 6) || args[0] != "--pipe" || args[2] != "--nonce" ||
    !Regex.IsMatch(args[1], @"\AOrchester-[A-F0-9]{32}\z") || !Regex.IsMatch(args[3], @"\A[A-F0-9]{64}\z")) return 2;
int expectedClient=0;
if(args.Length==6&&(args[4]!="--client"||!int.TryParse(args[5],out expectedClient)||expectedClient<=0))return 2;
using var timeout = new CancellationTokenSource(TimeSpan.FromHours(3));
try {
    var sid = WindowsIdentity.GetCurrent().User ?? throw new SafetyException("UnauthorizedRequest");
    var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false); security.SetOwner(sid);
    security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
    if(expectedClient>0)security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(NativePeer.OwnerSid(expectedClient)),PipeAccessRights.ReadWrite,AccessControlType.Allow));
    using var pipe = NamedPipeServerStreamAcl.Create(args[1], PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous, 4096, 4096, security);
    await pipe.WaitForConnectionAsync(timeout.Token);
    if(expectedClient>0&&NativePeer.ProcessId(pipe)!=expectedClient)throw new SafetyException("UnauthorizedRequest");
    var request = await HelperProtocol.ReadAsync<DiagnosticRequest>(pipe, timeout.Token);
    DiagnosticResponse response;
    try {
        HelperProtocol.Authenticate(request, args[3]);
        object data;
        switch (request.Command) {
            case DiagnosticCommand.InspectHost: data = HostInspector.Inspect(); break;
            case DiagnosticCommand.InspectFirmware:
                WindowsFirmwareVariables.EnableReadPrivilege();
                data = new UefiBootService(new WindowsFirmwareVariables()).Inspect(); break;
            case DiagnosticCommand.InspectStorage:
                data=await new WindowsStorageDiscovery(HostInspector.Inspect().SupportedWindows).DiscoverAsync(timeout.Token);break;
            case DiagnosticCommand.ValidatePlan:
                var plan = request.Plan ?? throw new SafetyException("UnauthorizedRequest");
                var host = HostInspector.Inspect();
                if (!host.SupportedWindows || !host.Uefi || !host.Is64Bit) throw new SafetyException("UnsupportedHost");
                var disk = (await new WindowsStorageDiscovery().DiscoverAsync(timeout.Token)).SingleOrDefault(d => d.Number == plan.OriginalDisk.Number)
                    ?? throw new SafetyException("DiskChanged");
                PlanGuard.Validate(plan, disk, request.Fingerprint!, false);
                data = new { Valid=true, Fingerprint=PlanGuard.Fingerprint(plan), NativeWritesEnabled=false }; break;
            case DiagnosticCommand.PrepareInstallation:
            case DiagnosticCommand.RequestInstallerBoot:
            case DiagnosticCommand.ListOperations:
            case DiagnosticCommand.CleanupOperation:
            case DiagnosticCommand.CompleteInstallation:
            case DiagnosticCommand.InspectOperation:
            case DiagnosticCommand.RestoreWindowsBoot:
            case DiagnosticCommand.ReclaimAllocation:
            case DiagnosticCommand.ExportOperation:
                data=await NativeDispatcher.ExecuteAsync(request,pipe,timeout.Token);break;
            default: throw new SafetyException("UnauthorizedRequest");
        }
        response = new(1, request.OperationId, true, null, JsonSerializer.SerializeToElement(data));
    } catch (SafetyException e) { response = new(1, request.OperationId, false, e.Code, null); }
      catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or CryptographicException or ArgumentException) { response = new(1, request.OperationId, false, "UnexpectedError", null); }
    await HelperProtocol.WriteAsync(pipe, response, timeout.Token);
    return response.Success ? 0 : 1;
} catch (Exception e) when (e is IOException or OperationCanceledException or SafetyException or UnauthorizedAccessException) { return 1; }
