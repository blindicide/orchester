using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Orchester.Adapters;
using Orchester.Boot;
using Orchester.Core;
using Orchester.Engine;
using Orchester.Images;
using Orchester.Recovery;
using Orchester.Security;

public static class AdvancedChecks {
    public static async Task<int> RunAsync() {
        int count=0;
        void Check(bool ok,string name) { if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);count++; }
        void Refuses(Action action,string code) { try{action();throw new Exception("Expected "+code);}catch(SafetyException e){Check(e.Code==code,code);} }
        async Task RefusesAsync(Func<Task> action,string code) {try{await action();throw new Exception("Expected "+code);}catch(SafetyException e){Check(e.Code==code,code);} }
        string root=Path.Combine(Path.GetTempPath(),"OrchesterAdvanced-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try {
            const long gib=1024L*1024*1024;
            byte[] iso=IsoFixture.Create();string imagePath=Path.Combine(root,"source.iso");await File.WriteAllBytesAsync(imagePath,iso);
            string hash=Convert.ToHexString(SHA256.HashData(iso));
            var image=await ImageVerifier.VerifyAsync(imagePath,hash,"ubuntu-test",TrustLevel.UserAuthenticatedDigest,default);
            var source=new PartitionSnapshot(3,Guid.NewGuid(),"{EBD0A0A2-B9E5-4433-87C0-68B6B72699C7}",gib,200*gib,"NTFS","C","Healthy",EncryptionState.Unencrypted,100*gib,false,true);
            var disk=new DiskSnapshot(0,"fixture-disk","Test",256*gib,512,4096,"GPT","NVMe","Healthy",false,false,[source]);
            var profile=new ReleaseProfile("ubuntu-test","Ubuntu Test","1","x86-64",new("https://example.org/iso"),new("https://example.org/sum"),"Subiquity","/casper/vmlinuz","/casper/initrd",25*gib,true,"0.1.0",false,[InstallationMode.Guided]);
            var plan=Planner.Create(disk,source.Id,40*gib,profile,image);
            Refuses(()=>WorkspaceSafety.ResolveChild(root,"../outside"),"UnsafePath");
            Refuses(()=>WorkspaceSafety.ResolveChild(root,"other:stream"),"UnsafePath");
            Refuses(()=>WorkspaceSafety.ResolveChild(root,"/absolute"),"UnsafePath");
            string secretFolder;
            await using(var secrets=new SecretWorkspace(root,plan.OperationId)) {
                secretFolder=secrets.DirectoryPath;
                string secret=await secrets.WriteAsync("user-data",new byte[]{1,2,3},default);
                Check((await File.ReadAllBytesAsync(secret)).Length==3,"restricted secret workspace writes");
                if(!OperatingSystem.IsWindows())Check((File.GetUnixFileMode(secret)&(UnixFileMode.GroupRead|UnixFileMode.OtherRead))==0,"secret permissions exclude other users");
            }
            Check(!Directory.Exists(secretFolder),"temporary secret workspace cleanup");
            var recipe=InstallerRecipes.Create(profile);
            Check(recipe.Strategy==BootStrategy.CasperIso&&!recipe.RequiresAdditionalHdMedia,"compiled Casper recipe");
            var debian=profile with {Id="debian-test",Installer="Debian Installer"};
            Check(InstallerRecipes.Create(debian).RequiresAdditionalHdMedia,"Debian DVD initrd is not treated as hd-media");
            var staging=new StagingWorkspace();string stagingRoot=Path.Combine(root,"bundles");
            var receipt=await staging.PrepareAsync(plan,profile,recipe,stagingRoot,false,default);
            string staged=Path.Combine(stagingRoot,plan.OperationId.ToString("D"));
            Check(receipt.Files.Length==4,"ISO and boot files extracted with hashes");
            await staging.VerifyAsync(staged,plan,profile,default);Check(true,"staging receipt verifies all files");
            await File.AppendAllTextAsync(Path.Combine(staged,"orchester","vmlinuz"),"changed");
            await RefusesAsync(()=>staging.VerifyAsync(staged,plan,profile,default),"StagingChanged");
            await RefusesAsync(()=>staging.PrepareAsync(plan,profile,recipe,stagingRoot,false,default),"DestinationExists");
            await RefusesAsync(()=>staging.PrepareAsync(plan with {Image=plan.Image with {Length=5*gib}},profile,recipe,Path.Combine(root,"fat32"),true,default),"Fat32FileLimit");
            await RefusesAsync(()=>staging.PrepareAsync(plan with {ProfileId=debian.Id,Image=plan.Image with {ProfileId=debian.Id}},debian,InstallerRecipes.Create(debian),Path.Combine(root,"debian"),false,default),"HdMediaRequired");
            var modifiedReceipt=receipt with {Files=receipt.Files.Select(f=>f.RelativePath=="orchester/vmlinuz"?f with {Sha256=new string('0',64)}:f).ToImmutableArray()};
            await File.WriteAllTextAsync(Path.Combine(staged,"staging.json"),JsonSerializer.Serialize(modifiedReceipt));
            await RefusesAsync(()=>staging.VerifyAsync(staged,plan,profile,default),"StagingChanged");
            var target=new AutomationTarget("NVME-SERIAL-1234",5,Guid.NewGuid(),6,Guid.NewGuid(),true,true);
            var options=new UnattendedOptions("en_US.UTF-8","us","Etc/UTC","orchester","tester","$6$abcdefgh$"+new string('A',86),target,["curl"]);
            var config=UnattendedGenerator.Generate(profile,plan,options);
            using(var payload=JsonDocument.Parse(config.Content[14..]))Check(payload.RootElement.GetProperty("autoinstall").GetProperty("identity").GetProperty("username").GetString()=="tester","Ubuntu cloud-config JSON is valid YAML data");
            Check(config.PreviewOnly&&!config.Content.Contains("wipe"),"automation preview has no whole-disk wipe");
            Refuses(()=>UnattendedGenerator.Generate(profile,plan,options with {PasswordHash="plaintext-password"}),"PasswordHashRequired");
            Refuses(()=>UnattendedGenerator.Generate(profile,plan,options with {Target=target with {EfiId=source.Id}}),"AutomationTargetUnsafe");
            Refuses(()=>UnattendedGenerator.Generate(profile,plan,options with {Packages=["curl\nmalicious"]}),"InvalidInstallerOptions");
            var preseed=UnattendedGenerator.Generate(debian,plan with {ProfileId=debian.Id},options);
            Check(!preseed.Content.Contains("partman-auto")&&!preseed.Content.Contains("grub-installer/bootdev"),"Debian preview does not guess a partitioning target");
            var owned=new OwnedPartition(plan.OperationId,disk.StableId,Guid.NewGuid(),4,source.Offset+source.Length-plan.StagingBytes,plan.StagingBytes,"{EBD0A0A2-B9E5-4433-87C0-68B6B72699C7}","NTFS");
            var firmware=new MemoryFirmware();firmware.Write("BootOrder",[1,0]);
            byte[] option=UefiLoadOption.Create(plan.OperationId,owned,512,@"\EFI\ORCHESTER\shim.efi");
            var parsed=UefiLoadOption.Parse(0x7000,option);
            Check(parsed.PartitionId==owned.PartitionId&&parsed.Active&&!parsed.WindowsBootManager,"UEFI GPT device path round trip");
            Refuses(()=>UefiLoadOption.Parse(1,[1,2,3]),"FirmwareMalformed");
            var boot=new UefiBootService(firmware);var bootOwned=boot.Register(plan.OperationId,owned,512,@"\EFI\ORCHESTER\shim.efi");
            Check(firmware.Read("BootOrder")!.SequenceEqual(new byte[]{1,0}),"register preserves persistent boot order");
            boot.RequestOneTimeBoot(bootOwned);Check(firmware.Read("BootNext")!.SequenceEqual(new byte[]{0,0x70}),"one-time BootNext selection");
            boot.RemoveOwned(bootOwned);Check(firmware.Read("Boot7000")==null&&firmware.Read("BootNext")==null,"verified owned boot cleanup");
            bootOwned=boot.Register(plan.OperationId,owned,512,@"\EFI\ORCHESTER\shim.efi");
            firmware.Write("BootNext",[2,0]);Refuses(()=>boot.RequestOneTimeBoot(bootOwned),"BootConfigurationChanged");
            var changed=firmware.Read("Boot7000")!;changed[^5]^=1;firmware.Write("Boot7000",changed);
            Refuses(()=>boot.RemoveOwned(bootOwned),"BootOwnershipMismatch");
            Refuses(ReleaseGate.RequireNativeWrites,"ExecutionUnavailable");
            var shrunk=disk with {Partitions=[source with {Length=source.Length-plan.RequestedBytes-plan.StagingBytes}]};
            StorageStatePolicy.ValidateAfterShrink(plan,shrunk);Check(true,"expected post-shrink geometry accepted");
            Refuses(()=>StorageStatePolicy.ValidateAfterShrink(plan,shrunk with {Partitions=[source]}),"LayoutChanged");
            var allocated=shrunk with {Partitions=[shrunk.Partitions[0],new(owned.Number,owned.PartitionId,owned.Type,owned.Offset,owned.Length,"NTFS","","Healthy",EncryptionState.Unencrypted,null,false,false)]};
            CleanupPolicy.Validate(plan,owned,allocated,false,false);Check(true,"cleanup requires exact recorded staging geometry");
            Refuses(()=>CleanupPolicy.Validate(plan,owned,allocated,true,false),"CleanupDependency");
            Refuses(()=>CleanupPolicy.Validate(plan,owned with {Offset=owned.Offset-gib},allocated,false,false),"OwnershipMismatch");
            string nonce=HelperProtocol.NewNonce();var request=new DiagnosticRequest(1,plan.OperationId,nonce,DiagnosticCommand.ValidatePlan,plan,PlanGuard.Fingerprint(plan));
            HelperProtocol.Authenticate(request,nonce);Check(true,"typed helper request bound to nonce and operation");
            Refuses(()=>HelperProtocol.Authenticate(request,HelperProtocol.NewNonce()),"UnauthorizedRequest");
            Refuses(()=>HelperProtocol.Authenticate(request with {Command=(DiagnosticCommand)999},nonce),"UnauthorizedRequest");
            using(var frames=new MemoryStream()) {await HelperProtocol.WriteAsync(frames,request,default);frames.Position=0;Check((await HelperProtocol.ReadAsync<DiagnosticRequest>(frames,default)).OperationId==plan.OperationId,"bounded IPC frame round trip");}
            using(var bad=new MemoryStream(new byte[]{255,255,255,127}))await RefusesAsync(()=>HelperProtocol.ReadAsync<DiagnosticRequest>(bad,default),"IpcFrameInvalid");
            var executable=plan with {DryRun=false};string executionRoot=Path.Combine(root,"execution");
            var backend=new FixtureBackend();var coordinator=new ExecutionCoordinator(executionRoot,backend);
            backend.BeforeStorage=async()=>Check((await coordinator.ReadAsync(plan.OperationId,default))[^1].Status=="INTENT","critical intent durable before storage backend");
            Check(await coordinator.PrepareAsync(executable,PlanGuard.Fingerprint(executable),default)==OperationPhase.BOOT_CONFIGURED,"persistent coordinator reaches boot configured");
            await coordinator.RequestBootAsync(executable,default);
            Check(await coordinator.InspectRecoveryAsync(plan.OperationId,default)==OperationPhase.REBOOT_REQUESTED,"boot request remains uncompleted");
            await RefusesAsync(()=>coordinator.PrepareAsync(executable,PlanGuard.Fingerprint(executable),default),"RecoveryInspectionRequired");
            var failed=executable with {OperationId=Guid.NewGuid()};var failing=new ExecutionCoordinator(executionRoot,new FixtureBackend {FailStorage=true});
            try{await failing.PrepareAsync(failed,PlanGuard.Fingerprint(failed),default);throw new Exception("Expected simulated interruption");}catch(IOException){}
            Check(await failing.InspectRecoveryAsync(failed.OperationId,default)==OperationPhase.RECOVERY_REQUIRED,"partial storage failure requires recovery");
            string recordPath=Path.Combine(executionRoot,plan.OperationId.ToString("D"),"execution.jsonl");await File.AppendAllTextAsync(recordPath,"{partial");
            Check(await coordinator.InspectRecoveryAsync(plan.OperationId,default)==OperationPhase.RECOVERY_REQUIRED,"truncated critical journal refuses resume");
            using(var key=RSA.Create(2048)) {
                var update=new ReleaseUpdateManifest(1,"0.3.0","win-x64",new("https://example.org/update.exe"),new string('A',64),new string('B',64));
                byte[] payload=JsonSerializer.SerializeToUtf8Bytes(update);byte[] signature=key.SignData(payload,HashAlgorithmName.SHA256,RSASignaturePadding.Pss);
                Check(SignedRelease.Verify(payload,signature,key,new Version(0,2,0)).Manifest.Version=="0.3.0","publisher-signed update metadata accepted");
                Refuses(()=>SignedRelease.Verify(payload,signature,key,new Version(0,4,0)),"UpdateRejected");
                payload[^2]^=1;Refuses(()=>SignedRelease.Verify(payload,signature,key,new Version(0,2,0)),"UpdateSignatureInvalid");
            }
            Guid installedRoot=Guid.NewGuid();var evidence=new InstalledSystemEvidence(plan.OperationId,installedRoot,disk.StableId,"ubuntu","1","ext4",true,true,true,true,true);
            var registry=new InstallationRegistry(Path.Combine(root,"installed"),new FixtureProbe(evidence));
            Check((await registry.ConfirmAsync(executable,profile,installedRoot,default)).Verified,"completion requires independent system and dual-boot evidence");
            var pending=new InstallationRegistry(Path.Combine(root,"pending"),new FixtureProbe(evidence with {LinuxBootVerified=false}));
            await RefusesAsync(()=>pending.ConfirmAsync(executable,profile,installedRoot,default),"CompletionEvidenceInsufficient");
        } finally {Directory.Delete(root,true);}
        return count;
    }
}
sealed class MemoryFirmware : IFirmwareVariables {
    private readonly Dictionary<string,byte[]> values=[];
    public byte[]? Read(string name)=>values.TryGetValue(name,out var value)?value.ToArray():null;
    public void Write(string name,byte[] value)=>values[name]=value.ToArray();
    public void Delete(string name)=>values.Remove(name);
}
sealed class FixtureBackend : IExecutionBackend {
    public Func<Task>? BeforeStorage {get;set;}
    public bool FailStorage {get;init;}
    public Task PreflightAsync(InstallationPlan plan,CancellationToken ct)=>Task.CompletedTask;
    public async Task<ExecutionArtifact> PrepareStorageAsync(InstallationPlan plan,CancellationToken ct) {if(BeforeStorage is not null)await BeforeStorage();if(FailStorage)throw new IOException("simulated interruption");return new(Guid.NewGuid(),new string('A',64));}
    public Task<ExecutionArtifact> StageInstallerAsync(InstallationPlan plan,CancellationToken ct)=>Task.FromResult(new ExecutionArtifact(Guid.NewGuid(),new string('B',64)));
    public Task<ExecutionArtifact> ConfigureBootAsync(InstallationPlan plan,CancellationToken ct)=>Task.FromResult(new ExecutionArtifact(Guid.NewGuid(),new string('C',64)));
    public Task RequestOneTimeBootAsync(InstallationPlan plan,CancellationToken ct)=>Task.CompletedTask;
}
sealed class FixtureProbe(InstalledSystemEvidence evidence) : IInstalledSystemProbe {
    public Task<InstalledSystemEvidence> InspectAsync(Guid operationId,CancellationToken ct)=>Task.FromResult(evidence);
}
