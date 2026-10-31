using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using Orchester.Core;
using Orchester.Engine;
using System.Windows;
using System.Windows.Controls;
using Orchester.App;
using Orchester.Localization;
public static class Program {
 [STAThread] public static void Main() {
  var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  app.StartupUri=null;
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
  var root=Path.Combine(Path.GetTempPath(),"OrchesterUi-"+Guid.NewGuid());Directory.CreateDirectory(root);
  try {
   const long gib=1024L*1024*1024;
   var source=new PartitionSnapshot(3,Guid.NewGuid(),"{EBD0A0A2-B9E5-4433-87C0-68B6B72699C7}",gib,200*gib,"NTFS","C","Healthy",EncryptionState.Unencrypted,100*gib,false,true);
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
