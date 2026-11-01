using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Orchester.Adapters;
using Orchester.Boot;
using Orchester.Core;
using Orchester.Engine;
using Orchester.Images;
using Orchester.Security;
using Orchester.Storage;

internal static class NativeDispatcher {
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint id);
    public static async Task<object> ExecuteAsync(DiagnosticRequest message,NamedPipeServerStream pipe,CancellationToken ct) {
        VerifyCaller(pipe);
        var store=new PrivilegedOperationStore();
        if(message.Command==DiagnosticCommand.ListOperations)return await store.ListAsync(ct);
        if(message.Command==DiagnosticCommand.InspectOperation)return await store.LoadAsync(message.OperationId,ct);
        if(message.Command==DiagnosticCommand.PrepareInstallation) {
            var request=message.Execution??throw new SafetyException("AuthorizationRequired");var plan=request.Plan;
            if(plan is null||plan.OriginalDisk is null||plan.Image is null||plan.Changes.IsDefault||plan.OriginalDisk.Partitions.IsDefault)throw new SafetyException("UnauthorizedRequest");
            request=request with {HdMediaKernel=plan.AuxiliaryImages.GetValueOrDefault("hd-kernel"),HdMediaInitrd=plan.AuxiliaryImages.GetValueOrDefault("hd-initrd")};
            if(plan.OperationId!=message.OperationId||request.Fingerprint!=PlanGuard.Fingerprint(plan)||!request.StorageConsent||!request.BackupConfirmed)throw new SafetyException("AuthorizationRequired");
            var profile=CatalogService.LoadBundled(Path.Combine(AppContext.BaseDirectory,"catalog.json")).SingleOrDefault(p=>p.Id==plan.ProfileId)??throw new SafetyException("AdapterMismatch");
            var host=HostInspector.Inspect();DistributionAdapter.For(profile).ValidateHost(host,profile);
            if(plan.DryRun||!NativeLayout.ForExecution(plan).Changes.SequenceEqual(plan.Changes))throw new SafetyException("PlanChanged");
            using var permit=ReleaseGate.Authorize(plan.OperationId);
            var current=(await new WindowsStorageDiscovery().DiscoverAsync(ct)).SingleOrDefault(d=>d.Number==plan.OriginalDisk.Number)??throw new SafetyException("DiskChanged");
            PlanGuard.Validate(plan,current,request.Fingerprint,true);
            await store.CreateAsync(plan,ct);
            using var lease=store.Lock(plan.OperationId);
            try {
                await CacheAsClient(pipe,plan.Image,Path.Combine(store.Folder(plan.OperationId),"source.iso"),ct);
                if(request.HdMediaKernel is not null)await CacheAsClient(pipe,request.HdMediaKernel,Path.Combine(store.Folder(plan.OperationId),"hd-vmlinuz"),ct);
                if(request.HdMediaInitrd is not null)await CacheAsClient(pipe,request.HdMediaInitrd,Path.Combine(store.Folder(plan.OperationId),"hd-initrd"),ct);
                var backend=new WindowsInstallationBackend(store,profile,request,async progress=> {
                    await HelperProtocol.WriteAsync(pipe,new DiagnosticResponse(1,message.OperationId,true,null,JsonSerializer.SerializeToElement(progress),true),CancellationToken.None);
                });
                var coordinator=new ExecutionCoordinator(store.Root,backend);
                await coordinator.PrepareAsync(plan,request.Fingerprint,ct);
                return Summary(await store.LoadAsync(plan.OperationId,ct));
            } catch(Exception error) {
                var state=await store.LoadAsync(plan.OperationId,CancellationToken.None);
                await store.SaveAsync(state with {Phase=state.Partitions.Length==0?OperationPhase.FAILED:OperationPhase.RECOVERY_REQUIRED,ErrorCode=error is SafetyException safety?safety.Code:"UnexpectedError"},CancellationToken.None);
                throw;
            }
        }
        var existing=await store.LoadAsync(message.OperationId,ct);
        using var control=store.Lock(message.OperationId);
        using var authorization=ReleaseGate.Authorize(message.OperationId);
        switch(message.Command) {
            case DiagnosticCommand.RequestInstallerBoot:
                if(existing.Phase!=OperationPhase.BOOT_CONFIGURED||existing.Boot is null)throw new SafetyException("InvalidTransition");
                await new NativeOperationLifecycle(store).VerifyStagedAsync(existing,ct);
                var bootProfile=CatalogService.LoadBundled(Path.Combine(AppContext.BaseDirectory,"catalog.json")).Single(p=>p.Id==existing.Plan.ProfileId);
                var bootBackend=new WindowsInstallationBackend(store,bootProfile,new(existing.Plan,PlanGuard.Fingerprint(existing.Plan),true,true));
                try {await new ExecutionCoordinator(store.Root,bootBackend).RequestBootAsync(existing.Plan,ct);}
                catch {await store.SaveAsync(existing with {Phase=OperationPhase.UNKNOWN_EXTERNAL_STATE},CancellationToken.None);throw;}
                return Summary(await store.LoadAsync(message.OperationId,ct));
            case DiagnosticCommand.CleanupOperation:
                return await new NativeOperationLifecycle(store).CleanupAsync(existing,ct);
            case DiagnosticCommand.CompleteInstallation:
                return await new NativeOperationLifecycle(store).ConfirmAsync(existing,message.LinuxBootConfirmed,ct);
            default:throw new SafetyException("UnauthorizedRequest");
        }
    }
    private static NativeOperationSummary Summary(NativeOperationState s)=>new(s.Plan.OperationId,s.Plan.ProfileId,s.Phase,s.Plan.Created,s.ErrorCode,s.Phase==OperationPhase.BOOT_CONFIGURED,true);
    private static async Task CacheAsClient(NamedPipeServerStream pipe,VerifiedImage source,string output,CancellationToken ct) {
        FileStream? input=null;
        pipe.RunAsClient(()=> {
            WorkspaceSafety.RejectReparsePoints(source.Path);
            input=new FileStream(source.Path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan);
        });
        using var opened=input??throw new SafetyException("ImageUnverified");
        if(opened.Length!=source.Length||source.Trust==TrustLevel.Unverified)throw new SafetyException("ImageUnverified");
        using(var destination=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,FileOptions.WriteThrough|FileOptions.Asynchronous)) {
            await opened.CopyToAsync(destination,1024*1024,ct);await destination.FlushAsync(ct);destination.Flush(true);
        }
        await ImageVerifier.VerifyAsync(output,source.Sha256,source.ProfileId,source.Trust,ct);
    }
    private static void VerifyCaller(NamedPipeServerStream pipe) {
        if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out uint pid))throw new SafetyException("UnauthorizedRequest");
        using var caller=Process.GetProcessById((int)pid);
        string expected=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"Orchester.App.exe"));
        string actual=Path.GetFullPath(caller.MainModule?.FileName??"");
        WorkspaceSafety.RejectReparsePoints(actual);
        if(!actual.Equals(expected,StringComparison.OrdinalIgnoreCase))throw new SafetyException("UnauthorizedRequest");
        System.Security.Cryptography.X509Certificates.X509Certificate2? signer=null;
        try{signer=AuthenticodeVerifier.LoadSigner(Environment.ProcessPath!);}catch(SafetyException){ }
#if PRODUCTION
        if(signer is null)throw new SafetyException("ExecutableSignatureInvalid");
#endif
        if(signer is not null)using(signer) {
            string pin=Convert.ToHexString(SHA256.HashData(signer.PublicKey.ExportSubjectPublicKeyInfo()));
            AuthenticodeVerifier.VerifyPublisher(Environment.ProcessPath!,pin);AuthenticodeVerifier.VerifyPublisher(actual,pin);
        }
    }
}
