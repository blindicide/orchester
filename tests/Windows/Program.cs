using System.Collections.Immutable;
using System.IO;
using System.Diagnostics;
using Orchester.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Orchester.Core;
using Orchester.Engine;
using Orchester.Storage;
using Orchester.Boot;
using System.Windows;
using System.Windows.Controls;
using Orchester.App;
using Orchester.Localization;
public static class Program {
 [STAThread] public static void Main() {
  var app=new App(false);app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  app.Dispatcher.BeginInvoke(new Action(async()=> {
   try { await CheckAsync(app); } catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; } finally { app.Shutdown(); }
  }));
  app.Run();
 }
 private static async Task CheckAsync(App app) {
  foreach(var culture in new[]{"en-US","ru-RU"}) {
   LanguageManager.Current.SetLanguage(culture);
   var window=new MainWindow();
   window.Measure(new Size(1180,820));window.Arrange(new Rect(0,0,1180,820));window.UpdateLayout();
   var model=(MainViewModel)window.DataContext;
   for(int section=0;section<5;section++){model.Section=section;window.UpdateLayout();}
   for(int stage=0;stage<7;stage++){model.Stage=stage;window.UpdateLayout();}
   foreach(bool dark in new[]{true,false}){model.Dark=dark;window.UpdateLayout();}
   model.Language=culture=="en-US"?"ru-RU":"en-US";
   if(LanguageManager.Current["Install"]=="Install" || window.FindName("LanguagePicker") is not ComboBox)throw new Exception("Resource or view load failed");
   window.Close();Console.WriteLine("PASS WPF shell, sections, stages, themes and live language switch: "+culture);
  }
  var host=HostInspector.Inspect();
  if(!host.Is64Bit || string.IsNullOrWhiteSpace(host.WindowsVersion) || host.WindowsVersion.Contains("Server",StringComparison.OrdinalIgnoreCase)&&host.SupportedWindows)throw new Exception("Windows host inspection failed");
  if(WindowsVolumeHealth.QueryDirty("C") is null)throw new Exception("Read-only NTFS dirty-state query unavailable on diagnostic runner");
  Console.WriteLine("PASS native read-only NTFS dirty-state query");
  var live=await new WindowsStorageDiscovery(includeSafetyDetails:false).DiscoverAsync(default);
  if(live.IsEmpty || live.Any(d=>d.Capacity<=0 || d.Partitions.Any(p=>p.Offset<0 || p.Length<=0)))throw new Exception("Read-only Windows storage discovery failed");
  Console.WriteLine($"PASS live read-only host and storage discovery: {live.Length} disks (no mutation)");
  string helper=Path.Combine(AppContext.BaseDirectory,"helper","Orchester.Helper.dll");
  foreach(bool authorized in new[]{true,false}) {
   string pipe="Orchester-"+Convert.ToHexString(RandomNumberGenerator.GetBytes(16));string nonce=HelperProtocol.NewNonce();
   var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true};
   foreach(string argument in new[]{helper,"--pipe",pipe,"--nonce",nonce})start.ArgumentList.Add(argument);
   using var process=Process.Start(start)??throw new Exception("Helper launch failed");
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
   var result=await HelperProtocol.ExchangeAsync(pipe,new(1,Guid.NewGuid(),authorized?nonce:HelperProtocol.NewNonce(),DiagnosticCommand.InspectHost,null,null),timeout.Token,process.Id);
   await process.WaitForExitAsync(timeout.Token);
   if(result.Success!=authorized || process.ExitCode!=(authorized?0:1))throw new Exception("Helper authentication failed");
  }
  Console.WriteLine("PASS native named-pipe helper exchange and unauthorized nonce rejection");
  try {AuthenticodeVerifier.VerifyPublisher(typeof(App).Assembly.Location,new string('A',64));throw new Exception("Unsigned executable accepted");}
  catch(Orchester.Core.SafetyException error) when(error.Code=="ExecutableSignatureInvalid") {Console.WriteLine("PASS unsigned executable rejected");}
  string? signedPath=Environment.GetEnvironmentVariable("ORCHESTER_TEST_SIGNED_EXE");
  string? certificatePath=Environment.GetEnvironmentVariable("ORCHESTER_TEST_SIGNER_CERT");
  if(signedPath is not null&&certificatePath is not null) {
   using var certificate=X509CertificateLoader.LoadCertificateFromFile(certificatePath);
   string publisher=Convert.ToHexString(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));
   AuthenticodeVerifier.VerifyPublisher(signedPath,publisher);
   try{AuthenticodeVerifier.VerifyPublisher(signedPath,new string('0',64));throw new Exception("Wrong publisher accepted");}
   catch(Orchester.Core.SafetyException error)when(error.Code=="ExecutableSignatureInvalid"){}
   using(var changed=new FileStream(signedPath,FileMode.Open,FileAccess.ReadWrite)){changed.Position=512;int original=changed.ReadByte();changed.Position=512;changed.WriteByte((byte)(original^0xFF));}
   try{AuthenticodeVerifier.VerifyPublisher(signedPath,publisher);throw new Exception("Modified signed executable accepted");}
   catch(Orchester.Core.SafetyException error)when(error.Code=="ExecutableSignatureInvalid"){}
   Console.WriteLine("PASS valid Authenticode publisher, wrong-publisher rejection and tamper rejection");
  }
  var root=Path.Combine(Path.GetTempPath(),"OrchesterUi-"+Guid.NewGuid());Directory.CreateDirectory(root);
  try {
   const long gib=1024L*1024*1024;
   var source=new PartitionSnapshot(3,Guid.NewGuid(),"{EBD0A0A2-B9E5-4433-87C0-68B6B72699C7}",gib,200*gib,"NTFS","C","Healthy",EncryptionState.Unencrypted,100*gib,false,true,false);
   var disk=new DiskSnapshot(0,"fixture-disk","Fixture",256*gib,512,4096,"GPT","NVMe","Healthy",false,false,[source]);
   var store=new OperationStore(Path.Combine(root,"operations"));
   var model=new MainViewModel(new FixtureStorage(disk),()=>new("Test Windows",true,true,true,true),store);
   await model.InitializeAsync();
   var iso=IsoFixture.Create();var path=Path.Combine(root,"fixture.iso");await File.WriteAllBytesAsync(path,iso);
   model.IsoPath=path;model.Digest=Convert.ToHexString(SHA256.HashData(iso));model.DigestTrusted=true;
   await ((AsyncCommand)model.VerifyCommand).ExecuteAsync();
   model.Stage=3;await ((AsyncCommand)model.NextCommand).ExecuteAsync();
   if(model.Stage!=4 || model.Changes.Count!=3)throw new Exception("Dry-run preview failed: "+model.Status);
   model.Approved=true;await ((AsyncCommand)model.SavePlanCommand).ExecuteAsync();
   var id=store.ListOperations().Single();
   if((await store.LoadAsync(id,default)).Phase!=OperationPhase.PLAN_APPROVED || model.Stage!=5)throw new Exception("Dry-run journal phase failed");
   model.Settings.StagingDirectory=Path.Combine(root,"bundle");
   await ((AsyncCommand)model.PrepareBundleCommand).ExecuteAsync();
   await ((AsyncCommand)model.VerifyBundleCommand).ExecuteAsync();
   if(!File.Exists(Path.Combine(model.Settings.StagingDirectory,id.ToString("D"),"staging.json")))throw new Exception("UI bundle preparation failed: "+model.Status);
   Console.WriteLine("PASS UI staging bundle preparation and verification");
   model.Language="ru-RU";if(!model.Operations.Single().Contains("План утверждён"))throw new Exception("Live operation localization failed");
   await ((AsyncCommand)model.NextCommand).ExecuteAsync();
   if(model.Stage!=6 || (await store.LoadAsync(id,default)).Phase!=OperationPhase.PLAN_APPROVED)throw new Exception("False installation completion");
   await ((AsyncCommand)model.CancelCommand).ExecuteAsync();
   if((await store.LoadAsync(id,default)).Phase!=OperationPhase.CANCELLED)throw new Exception("Dry-run cancellation failed");
   Console.WriteLine("PASS complete wizard dry run: verified ISO, plan approval, durable history, live localization, gated handoff and cancellation");
  } finally {Directory.Delete(root,true);}
 }
}

sealed class FixtureStorage(DiskSnapshot disk) : IStorageDiscovery {
 public Task<ImmutableArray<DiskSnapshot>> DiscoverAsync(CancellationToken ct)=>Task.FromResult(ImmutableArray.Create(disk));
}
