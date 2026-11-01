using System.Collections.Immutable;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Orchester.Core;
using Orchester.Security;
namespace Orchester.Engine;

[SupportedOSPlatform("windows")]
public sealed class PrivilegedOperationStore {
    public string Root {get;}
    private readonly string ownerSid;
    private static readonly SecurityIdentifier Admins=new(WellKnownSidType.BuiltinAdministratorsSid,null);
    private static readonly SecurityIdentifier SystemAccount=new(WellKnownSidType.LocalSystemSid,null);
    public PrivilegedOperationStore(string? ownerSid=null) {
        this.ownerSid=ownerSid??WindowsIdentity.GetCurrent().User?.Value??throw new SafetyException("UnauthorizedRequest");
        string parent=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Orchester");
        WorkspaceSafety.RejectReparsePoints(parent);
        if(Directory.Exists(parent)) {
            ValidateAcl(parent);
            var parentOwner=new DirectoryInfo(parent).GetAccessControl().GetOwner(typeof(SecurityIdentifier));
            if(!Admins.Equals(parentOwner)&&!SystemAccount.Equals(parentOwner))throw new SafetyException("OperationStoreUntrusted");
        } else Directory.CreateDirectory(parent);
        Protect(parent);
        Root=Path.Combine(parent,"Operations");
        WorkspaceSafety.RejectReparsePoints(Root);
        if(Directory.Exists(Root)) {
            ValidateAcl(Root);
            var owner=new DirectoryInfo(Root).GetAccessControl().GetOwner(typeof(SecurityIdentifier));
            if(!Admins.Equals(owner)&&!SystemAccount.Equals(owner))throw new SafetyException("OperationStoreUntrusted");
        } else {Directory.CreateDirectory(Root);Protect(Root);}
    }
    private static void ValidateAcl(string path) {
        var acl=new DirectoryInfo(path).GetAccessControl();
        var writes=FileSystemRights.Write|FileSystemRights.Delete|FileSystemRights.DeleteSubdirectoriesAndFiles|FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership;
        foreach(FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(SecurityIdentifier)))
            if(rule.AccessControlType==AccessControlType.Allow&&!Admins.Equals(rule.IdentityReference)&&!SystemAccount.Equals(rule.IdentityReference)&&(rule.FileSystemRights&writes)!=0)throw new SafetyException("OperationStoreUntrusted");
    }
    private static void Protect(string path) {
        var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);acl.SetOwner(Admins);
        foreach(var sid in new[]{Admins,SystemAccount})acl.AddAccessRule(new(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        acl.AddAccessRule(new(new SecurityIdentifier("S-1-3-4"),FileSystemRights.ReadPermissions,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(acl);
    }
    public FileStream LockDisk(string identity)=>new(Path.Combine(Root,"disk-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)))+".lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    public async Task EnsureDiskAvailableAsync(InstallationPlan plan,CancellationToken ct) {
        foreach(string folder in Directory.EnumerateDirectories(Root)) {
            if(!Guid.TryParse(Path.GetFileName(folder),out _))continue;
            var state=JsonSerializer.Deserialize<NativeOperationState>(await File.ReadAllTextAsync(Path.Combine(folder,"operation.json"),ct))??throw new SafetyException("JournalIncomplete");
            if(state.Plan.OriginalDisk.StableId!=plan.OriginalDisk.StableId)continue;
            bool resources=state.Partitions.Any(p=>!state.RetainedPartitions.Contains(p.PartitionId));
            if(resources||state.Phase is not (OperationPhase.COMPLETED or OperationPhase.CANCELLED or OperationPhase.FAILED))throw new SafetyException("StorageBusy");
        }
    }
    public string Folder(Guid operationId)=>WorkspaceSafety.ResolveChild(Root,operationId.ToString("D"));
    public async Task CreateAsync(InstallationPlan plan,CancellationToken ct) {
        string folder=Folder(plan.OperationId);
        if(Directory.Exists(folder))throw new SafetyException("OperationExists");
        Directory.CreateDirectory(folder);Protect(folder);
        var sid=ownerSid;
        await SaveAsync(new(1,plan,sid,DateTimeOffset.UtcNow,[],[],null,OperationPhase.PLAN_APPROVED,null,null,[]),ct);
    }
    public async Task SaveAsync(NativeOperationState state,CancellationToken ct) {
        string folder=Folder(state.Plan.OperationId);WorkspaceSafety.RejectReparsePoints(folder);
        string path=Path.Combine(folder,"operation.json");
        using(var file=new FileStream(path+".tmp",FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) {
            await JsonSerializer.SerializeAsync(file,state,cancellationToken:ct);await file.FlushAsync(ct);file.Flush(true);
        }
        File.Move(path+".tmp",path,true);
    }
    public async Task<NativeOperationState> LoadAsync(Guid id,CancellationToken ct) {
        var state=JsonSerializer.Deserialize<NativeOperationState>(await File.ReadAllTextAsync(Path.Combine(Folder(id),"operation.json"),ct))??throw new SafetyException("JournalIncomplete");
        if(state.SchemaVersion!=1||state.Plan.OperationId!=id||state.OwnerSid!=ownerSid)throw new SafetyException("UnauthorizedRequest");
        return state;
    }
    public FileStream Lock(Guid id)=>new(Path.Combine(Folder(id),"control.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    public async Task<bool> CanUninstallAsync(CancellationToken ct) {
        foreach(string folder in Directory.EnumerateDirectories(Root)) {
            if(!Guid.TryParse(Path.GetFileName(folder),out _))continue;
            try {
                using var lease=new FileStream(Path.Combine(folder,"control.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                var state=JsonSerializer.Deserialize<NativeOperationState>(await File.ReadAllTextAsync(Path.Combine(folder,"operation.json"),ct));
                if(state is null||state.SchemaVersion!=1||state.Boot is not null||state.Mounts.Length>0||state.Partitions.Any(p=>!state.RetainedPartitions.Contains(p.PartitionId))||state.Phase is OperationPhase.RECOVERY_REQUIRED or OperationPhase.UNKNOWN_EXTERNAL_STATE)return false;
            }catch(Exception e)when(e is IOException or JsonException){return false;}
        }
        return true;
    }
    public async Task<ImmutableArray<NativeOperationSummary>> ListAsync(CancellationToken ct) {
        var rows=ImmutableArray.CreateBuilder<NativeOperationSummary>();
        foreach(string folder in Directory.EnumerateDirectories(Root)) {
            if(!Guid.TryParse(Path.GetFileName(folder),out var id))continue;
            try {
                using var control=Lock(id);
                var s=await LoadAsync(id,ct);
                if(s.Phase is not (OperationPhase.COMPLETED or OperationPhase.CANCELLED)&&File.Exists(Path.Combine(folder,"execution.jsonl"))) {
                    var recorded=await new ExecutionCoordinator(Root,null).InspectRecoveryAsync(id,ct);
                    if(recorded is OperationPhase.RECOVERY_REQUIRED or OperationPhase.UNKNOWN_EXTERNAL_STATE){s=s with {Phase=recorded};}
                }
                rows.Add(new(id,s.Plan.ProfileId,s.Phase,s.Plan.Created,s.ErrorCode,s.Phase==OperationPhase.BOOT_CONFIGURED,s.Partitions.Length>0));}
            catch(SafetyException e)when(e.Code=="UnauthorizedRequest") { }
            catch(Exception e)when(e is IOException or JsonException or SafetyException) {rows.Add(new(id,"",OperationPhase.RECOVERY_REQUIRED,DateTimeOffset.MinValue,"JournalIncomplete",false,false));}
        }
        return rows.ToImmutable();
    }
}
