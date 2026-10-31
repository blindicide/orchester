using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Orchester.Core;
using Orchester.Engine;
using Orchester.Images;
using Orchester.Recovery;
using Orchester.Security;
using Orchester.Adapters;
const long GiB=1024L*1024*1024;
var root=Path.Combine(Path.GetTempPath(),"OrchesterTests-"+Guid.NewGuid());Directory.CreateDirectory(root);
int passed=0;
void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
void Refuses(Action action,string code){try{action();throw new Exception("Expected "+code);}catch(SafetyException e){Check(e.Code==code,code);}}
async Task RefusesAsync(Func<Task> action,string code){try{await action();throw new Exception("Expected "+code);}catch(SafetyException e){Check(e.Code==code,code);}}
try {
 var bytes=Encoding.UTF8.GetBytes("publisher authenticated test fixture");var path=Path.Combine(root,"image.iso");await File.WriteAllBytesAsync(path,bytes);
 var hash=Convert.ToHexString(SHA256.HashData(bytes));
 var image=await ImageVerifier.VerifyAsync(path,hash,"ubuntu-test",TrustLevel.UserAuthenticatedDigest,default);
 Check(image.Length==bytes.Length,"streamed verification");
 await RefusesAsync(()=>ImageVerifier.VerifyAsync(path,new string('0',64),"ubuntu-test",TrustLevel.UserAuthenticatedDigest,default),"ChecksumMismatch");
 await RefusesAsync(()=>ImageVerifier.VerifyAsync(path,hash,"ubuntu-test",TrustLevel.Unverified,default),"TrustedDigestRequired");
 var partition=new PartitionSnapshot(3,Guid.NewGuid(),"{EBD0A0A2-B9E5-4433-87C0-68B6B72699C7}",GiB,200*GiB,"NTFS","C","Healthy",EncryptionState.Unencrypted,100*GiB,false,true);
 var disk=new DiskSnapshot(0,"stable-test-device","Test disk",256*GiB,512,4096,"GPT","NVMe","Healthy",false,false,[partition]);
 var profile=new ReleaseProfile("ubuntu-test","Ubuntu Test","1","x86-64",new("https://example.org/image.iso"),new("https://example.org/SHA256SUMS"),"Subiquity","/casper/vmlinuz","/casper/initrd",25*GiB,true,"0.1.0",false,[InstallationMode.Guided]);
 var plan=Planner.Create(disk,partition.Id,40*GiB,profile,image);var fingerprint=PlanGuard.Fingerprint(plan);
 PlanGuard.Validate(plan,disk,fingerprint,false);Check(plan.DryRun,"valid immutable dry-run plan");
 Refuses(()=>Planner.Create(disk,partition.Id,100*GiB,profile,image),"ShrinkLimit");
 Refuses(()=>PlanGuard.Validate(plan,disk with { StableId="replacement" },fingerprint,false),"DiskChanged");
 Refuses(()=>PlanGuard.Validate(plan,disk with { Partitions=[partition with { Length=199*GiB }] },fingerprint,false),"LayoutChanged");
 Refuses(()=>PlanGuard.Validate(plan with { RequestedBytes=41*GiB },disk,fingerprint,false),"PlanChanged");
 Refuses(()=>Planner.Create(disk with { Partitions=[partition with { IsSystem=true }] },partition.Id,40*GiB,profile,image),"UnsafeSource");
 Refuses(()=>Planner.Create(disk with { Partitions=[partition with { Encryption=EncryptionState.Unknown }] },partition.Id,40*GiB,profile,image),"UnsafeSource");
 Refuses(()=>Planner.Create(disk with { Partitions=[partition with { Encryption=EncryptionState.Protected }] },partition.Id,40*GiB,profile,image),"UnsafeSource");
 Refuses(()=>Planner.Create(disk with { Partitions=[partition with { ShrinkBytes=null }] },partition.Id,40*GiB,profile,image),"UnsafeSource");
 Refuses(()=>Planner.Create(disk with { Partitions=[partition with { Type="DE94BBA4-06D1-4D40-A16A-BFD50179D6AC" }] },partition.Id,40*GiB,profile,image),"UnsafeSource");
 Refuses(()=>Planner.Create(disk with { BusType="RAID" },partition.Id,40*GiB,profile,image),"UnsupportedDisk");
 Refuses(()=>PlanGuard.ValidateDisk(disk with { Partitions=[partition,partition with {Number=4,Id=Guid.NewGuid()}] }),"InvalidGeometry");
 var malicious=plan with {Changes=[new("FormatEsp",Guid.NewGuid(),0,GiB,true)]};
 Refuses(()=>PlanGuard.Validate(malicious,disk,PlanGuard.Fingerprint(malicious),false),"PlanChanged");
 Refuses(()=>PlanGuard.Validate(plan,disk,fingerprint,true),"ExecutionUnavailable");
 Refuses(()=>HelperPolicy.Authorize(new(1,Guid.NewGuid(),fingerprint,HelperCommand.Inspect),plan,disk),"UnauthorizedRequest");
 Refuses(()=>DistributionAdapter.For(profile).ValidateHost(new("Windows",true,true,true,true),profile with {SecureBoot=false}),"SecureBootUnsupported");
 Refuses(()=>DistributionAdapter.For(profile).ValidateRelease(profile,true),"ReleaseUnvalidated");
 Check(!OperationStore.CanTransition(OperationPhase.REBOOT_REQUESTED,OperationPhase.COMPLETED),"reboot is not completion evidence");
 Check(!OperationStore.CanTransition(OperationPhase.COMPLETED,OperationPhase.PREFLIGHT),"terminal phase stays terminal");
 Check(!OperationStore.CanTransition(OperationPhase.STORAGE_PREPARED,OperationPhase.CANCELLED),"no false rollback after storage mutation");
 var store=new OperationStore(Path.Combine(root,"operations"));await store.SaveDryRunAsync(plan,default);
 Check((await store.LoadAsync(plan.OperationId,default)).Phase==OperationPhase.PREFLIGHT,"durable manifest and journal");
 await store.TransitionAsync(plan.OperationId,OperationPhase.IMAGE_READY,"ImageVerified",default);
 await store.TransitionAsync(plan.OperationId,OperationPhase.PLAN_APPROVED,"PlanApproved",default);
 await RefusesAsync(()=>store.TransitionAsync(plan.OperationId,OperationPhase.STORAGE_PREPARED,"unsafe",default),"ExecutionUnavailable");
 await store.TransitionAsync(plan.OperationId,OperationPhase.CANCELLED,"OperationCancelled",default);
 Check((await store.LoadAsync(plan.OperationId,default)).Phase==OperationPhase.CANCELLED,"safe reversible cancellation");
 var report=Path.Combine(root,"diagnostics.json");await new RecoveryService(store).ExportRedactedAsync(report,new("Test Windows",true,true,true,true),[disk],default);
 var reportText=await File.ReadAllTextAsync(report);Check(!reportText.Contains(path)&&!reportText.Contains(disk.StableId),"diagnostic allowlist redacts paths and device identity");
 await File.AppendAllTextAsync(Path.Combine(root,"operations",plan.OperationId.ToString(),"journal.jsonl"),"{truncated");
 Check(!(await new RecoveryService(store).InspectAsync(default))[0].Consistent,"interrupted journal requires recovery");
 var isoPath=Path.Combine(root,"fixture.iso");await File.WriteAllBytesAsync(isoPath,IsoFixture.Create());
 var goodIso=await IsoInspector.InspectAsync(isoPath,profile,default);Check(goodIso.Compatible,"valid x64 ISO profile inspection");
 var wrongArch=IsoFixture.Create();wrongArch[25*2048+128+4]=0x4c;wrongArch[25*2048+128+5]=0x01;await File.WriteAllBytesAsync(isoPath,wrongArch);
 Check(!(await IsoInspector.InspectAsync(isoPath,profile,default)).Compatible,"x86 EFI rejected for x64 image");
 var malformed=await IsoInspector.InspectAsync(path,profile,default);Check(!malformed.Compatible,"unsupported image rejected");
 // Exercise download resume when a server ignores the Range header: old bytes must be discarded.
 string destination=Path.Combine(root,"download.iso");await File.WriteAllBytesAsync(destination+"."+hash.ToLowerInvariant()+".partial",[9,9,9]);
 using var http=new HttpClient(new FakeHttp(bytes));
 var downloaded=await new DownloadEngine(http).DownloadAsync(new("https://example.org/image.iso"),destination,hash,profile.Id,TrustLevel.UserAuthenticatedDigest,null,0,default);
 Check((await File.ReadAllBytesAsync(downloaded.Path)).SequenceEqual(bytes),"range ignored safely restarts download");
 string destination2=Path.Combine(root,"resume.iso");await File.WriteAllBytesAsync(destination2+"."+hash.ToLowerInvariant()+".partial",bytes[..5]);
 using var ranged=new HttpClient(new FakeHttp(bytes,true));
 await new DownloadEngine(ranged).DownloadAsync(new("https://example.org/image.iso"),destination2,hash,profile.Id,TrustLevel.UserAuthenticatedDigest,null,0,default);
 Check((await File.ReadAllBytesAsync(destination2)).SequenceEqual(bytes),"valid partial content resumes and rehashes entire image");
 await RefusesAsync(()=>new DownloadEngine(http).DownloadAsync(new("https://example.org/image.iso"),destination,new string('0',64),profile.Id,TrustLevel.UserAuthenticatedDigest,null,0,default),"DestinationExists");
 Check((await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes),"existing destination preserved");
 string completedDestination=Path.Combine(root,"complete.iso");await File.WriteAllBytesAsync(completedDestination+"."+hash.ToLowerInvariant()+".partial",bytes);
 using var completeHttp=new HttpClient(new CompleteHttp());
 await new DownloadEngine(completeHttp).DownloadAsync(new("https://example.org/image.iso"),completedDestination,hash,profile.Id,TrustLevel.UserAuthenticatedDigest,null,0,default);
 Check((await File.ReadAllBytesAsync(completedDestination)).SequenceEqual(bytes),"completed partial survives server 416 and is fully verified");
 using var rsa=RSA.Create(2048);var catalog=Encoding.UTF8.GetBytes("{}");
 Refuses(()=>CatalogService.VerifyUpdate(catalog,new byte[256],rsa),"CatalogSignatureInvalid");
 Console.WriteLine($"{passed} checks passed.");
} finally {Directory.Delete(root,true);}
sealed class FakeHttp(byte[] data,bool range=false) : HttpMessageHandler {
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
  int start=range?(int)(request.Headers.Range?.Ranges.First().From??0):0;
  var response=new HttpResponseMessage(range?HttpStatusCode.PartialContent:HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(data[start..])};
  if(range)response.Content.Headers.ContentRange=new ContentRangeHeaderValue(start,data.Length-1,data.Length);
  return Task.FromResult(response);
 }
}

sealed class CompleteHttp : HttpMessageHandler {
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable){RequestMessage=request,Content=new ByteArrayContent([])});
}
