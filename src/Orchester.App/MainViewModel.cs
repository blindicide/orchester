using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Orchester.Adapters;
using Orchester.Boot;
using Orchester.Core;
using Orchester.Engine;
using Orchester.Images;
using Orchester.Localization;
using Orchester.Recovery;
using Orchester.Security;
using Orchester.Storage;
namespace Orchester.App;
public sealed class AsyncCommand(Func<Task> action,Func<bool>? enabled=null) : ICommand {
 public bool CanExecute(object? parameter)=>enabled?.Invoke()??true;
 public async void Execute(object? parameter)=>await action();
 public event EventHandler? CanExecuteChanged { add=>CommandManager.RequerySuggested+=value; remove=>CommandManager.RequerySuggested-=value; }
}
public sealed record Choice<T>(T Value,string Label);
public sealed record MapSegment(string Label,double Width,bool Proposed);
public sealed class MainViewModel : INotifyPropertyChanged {
 private readonly LanguageManager l=LanguageManager.Current;
 private readonly OperationStore store=new(Path.Combine(App.DataRoot,"Operations"));
 private readonly WindowsStorageDiscovery storage=new();
 private static readonly HttpClient Http=new(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=Timeout.InfiniteTimeSpan };
 private CancellationTokenSource? active;
 private InstallationPlan? plan;
 private string? approvedFingerprint;
 private Guid? savedOperation;
 private VerifiedImage? image;
 private HostSnapshot? host;
 private int stage,section;
 private bool busy,approved,digestTrusted;
 private string status="Ready",statusDetail="",distribution="",isoPath="",digest="",size="40";
 private ReleaseProfile? release;
 private DiskSnapshot? disk;
 private PartitionSnapshot? partition;
 public event PropertyChangedEventHandler? PropertyChanged;
 public UserSettings Settings { get; }=UserSettings.Load();
 public ObservableCollection<string> Distributions { get; }=[];
 public ImmutableArray<ReleaseProfile> Catalog { get; }
 public ObservableCollection<ReleaseProfile> Releases { get; }=[];
 public ObservableCollection<Choice<DiskSnapshot>> Disks { get; }=[];
 public ObservableCollection<Choice<PartitionSnapshot>> Partitions { get; }=[];
 public ObservableCollection<MapSegment> CurrentMap { get; }=[];
 public ObservableCollection<MapSegment> ProposedMap { get; }=[];
 public ObservableCollection<string> Changes { get; }=[];
 public ObservableCollection<string> Operations { get; }=[];
 public ObservableCollection<string> Inventory { get; }=[];
 public string[] Languages { get; }=["en-US","ru-RU"];
 public string Language { get=>l.Language; set { l.SetLanguage(value); Settings.Language=l.Language; Changed(); RefreshText(); } }
 public bool Dark { get=>Settings.Dark; set { Settings.Dark=value; App.ApplyTheme(value); Changed(); } }
 public int Section { get=>section; set {section=value;Changed();Changed(nameof(InstallVisibility));Changed(nameof(ManageVisibility));Changed(nameof(DisksVisibility));Changed(nameof(RecoveryVisibility));Changed(nameof(SettingsVisibility));} }
 public Visibility InstallVisibility=>Section==0?Visibility.Visible:Visibility.Collapsed;
 public Visibility ManageVisibility=>Section==1?Visibility.Visible:Visibility.Collapsed;
 public Visibility DisksVisibility=>Section==2?Visibility.Visible:Visibility.Collapsed;
 public Visibility RecoveryVisibility=>Section==3?Visibility.Visible:Visibility.Collapsed;
 public Visibility SettingsVisibility=>Section==4?Visibility.Visible:Visibility.Collapsed;
 public int Stage { get=>stage; set {stage=Math.Clamp(value,0,6);Changed();Changed(nameof(StageTitle));for(int i=1;i<=7;i++)Changed("Stage"+i+"Visibility"); CommandManager.InvalidateRequerySuggested();} }
 public string StageTitle=>l["Stage"+(Stage+1)];
 public Visibility Stage1Visibility=>Stage==0?Visibility.Visible:Visibility.Collapsed;
 public Visibility Stage2Visibility=>Stage==1?Visibility.Visible:Visibility.Collapsed;
 public Visibility Stage3Visibility=>Stage==2?Visibility.Visible:Visibility.Collapsed;
 public Visibility Stage4Visibility=>Stage==3?Visibility.Visible:Visibility.Collapsed;
 public Visibility Stage5Visibility=>Stage==4?Visibility.Visible:Visibility.Collapsed;
 public Visibility Stage6Visibility=>Stage==5?Visibility.Visible:Visibility.Collapsed;
 public Visibility Stage7Visibility=>Stage==6?Visibility.Visible:Visibility.Collapsed;
 public bool Busy { get=>busy; private set {busy=value;Changed();Changed(nameof(Idle));CommandManager.InvalidateRequerySuggested();} }
 public bool Idle=>!Busy;
 public string Status=>l[status]+statusDetail;
 public string HostText=>host is null?l["HostUnavailable"]:string.Format(l["HostSummary"],host.WindowsVersion,l[host.Uefi?"Yes":"No"],l[host.SecureBoot is null?"Unknown":host.SecureBoot==true?"Yes":"No"]);
 public string Distribution { get=>distribution; set {distribution=value; Releases.Clear();foreach(var p in Catalog.Where(x=>x.Distribution==value))Releases.Add(p);Release=Releases.FirstOrDefault();Changed();} }
 public ReleaseProfile? Release { get=>release; set {release=value;image=null;InvalidatePlan();Changed();Changed(nameof(DownloadUrl));Changed(nameof(ChecksumUrl));} }
 public string DownloadUrl=>Release?.DownloadUrl.ToString()??"";
 public string ChecksumUrl=>Release?.ChecksumUrl.ToString()??"";
 public string IsoPath { get=>isoPath;set {isoPath=value;image=null;InvalidatePlan();Changed();} }
 public string Digest { get=>digest;set {digest=value.Trim();image=null;InvalidatePlan();Changed();} }
 public bool DigestTrusted { get=>digestTrusted;set {digestTrusted=value;image=null;InvalidatePlan();Changed();} }
 public Choice<DiskSnapshot>? SelectedDisk { get=>Disks.FirstOrDefault(d=>d.Value==disk);set {disk=value?.Value;Partitions.Clear();if(disk is not null)foreach(var p in disk.Partitions)Partitions.Add(new(p,PartitionLabel(p)));SelectedPartition=Partitions.FirstOrDefault(p=>p.Value.FileSystem=="NTFS"&&!p.Value.Protected);InvalidatePlan();RefreshMaps();Changed();} }
 public Choice<PartitionSnapshot>? SelectedPartition { get=>Partitions.FirstOrDefault(p=>p.Value==partition);set {partition=value?.Value;InvalidatePlan();Changed();} }
 public string SizeGiB { get=>size;set {size=value;InvalidatePlan();Changed();} }
 public bool Approved { get=>approved;set {approved=value;Changed();CommandManager.InvalidateRequerySuggested();} }
 public ICommand RefreshCommand { get; }
 public ICommand VerifyCommand { get; }
 public ICommand BrowseCommand { get; }
 public ICommand DownloadCommand { get; }
 public ICommand PlanCommand { get; }
 public ICommand SavePlanCommand { get; }
 public ICommand BackCommand { get; }
 public ICommand NextCommand { get; }
 public ICommand CancelCommand { get; }
 public ICommand InspectCommand { get; }
 public ICommand ExportCommand { get; }
 public ICommand SaveSettingsCommand { get; }
 public MainViewModel() {
  Catalog=CatalogService.LoadBundled(Path.Combine(AppContext.BaseDirectory,"catalog.json"));
  foreach(var d in Catalog.Select(p=>p.Distribution).Distinct()) Distributions.Add(d);
  Distribution=Distributions[0];
  RefreshCommand=new AsyncCommand(()=>Run(RefreshAsync),()=>Idle);
  VerifyCommand=new AsyncCommand(()=>Run(VerifyAsync),()=>Idle);
  BrowseCommand=new AsyncCommand(()=> {var dialog=new OpenFileDialog {Filter="ISO (*.iso)|*.iso",CheckFileExists=true};if(dialog.ShowDialog()==true)IsoPath=dialog.FileName;return Task.CompletedTask;},()=>Idle);
  DownloadCommand=new AsyncCommand(()=>Run(DownloadAsync),()=>Idle);
  PlanCommand=new AsyncCommand(()=>Run(CreatePlanAsync),()=>Idle);
  SavePlanCommand=new AsyncCommand(()=>Run(SavePlanAsync),()=>Idle&&Approved&&plan is not null&&savedOperation is null);
  BackCommand=new AsyncCommand(()=> {Stage--;return Task.CompletedTask;},()=>Idle&&Stage>0);
  NextCommand=new AsyncCommand(()=>Run(AdvanceAsync),()=>Idle&&Stage<6);
  CancelCommand=new AsyncCommand(CancelAsync,()=>Busy || savedOperation is not null);
  InspectCommand=new AsyncCommand(()=>Run(InspectAsync),()=>Idle);
  ExportCommand=new AsyncCommand(()=>Run(ExportAsync),()=>Idle);
  SaveSettingsCommand=new AsyncCommand(()=>Run(_=> {Settings.Language=Language;Settings.Save();SetStatus("SettingsSaved");return Task.CompletedTask;}),()=>Idle);
  l.PropertyChanged+=(_,_)=>RefreshText();
 }
 public async Task InitializeAsync()=>await Run(RefreshAsync);
 private async Task Run(Func<CancellationToken,Task> action) {
  if(Busy)return;Busy=true;active=new();SetStatus("Busy");
  try {await action(active.Token);if(status=="Busy")SetStatus("Ready");}
  catch(OperationCanceledException){SetStatus("Cancelled");}
  catch(SafetyException e){SetStatus(e.Code);}
  catch(Exception e) when(e is IOException or UnauthorizedAccessException or HttpRequestException or System.Text.Json.JsonException or ArgumentException or OverflowException or InvalidOperationException) {SetStatus("UnexpectedError");}
  finally {active.Dispose();active=null;Busy=false;}
 }
 private void SetStatus(string code,string detail="") {status=code;statusDetail=detail;Changed(nameof(Status));}
 private void InvalidatePlan() {plan=null;approvedFingerprint=null;Approved=false;savedOperation=null;Changes.Clear();ProposedMap.Clear();}
 private async Task RefreshAsync(CancellationToken ct) {
  InvalidatePlan();host=HostInspector.Inspect();Changed(nameof(HostText));
  var disks=await storage.DiscoverAsync(ct);Disks.Clear();
  foreach(var d in disks)Disks.Add(new(d,DiskLabel(d)));
  SelectedDisk=Disks.FirstOrDefault();RefreshInventory();await InspectAsync(ct);
 }
 private async Task VerifyAsync(CancellationToken ct) {
  image=null;InvalidatePlan();if(!DigestTrusted)throw new SafetyException("TrustedDigestRequired");
  var selected=Release??throw new SafetyException("AdapterMismatch");
  var verified=await ImageVerifier.VerifyAsync(IsoPath,Digest,selected.Id,TrustLevel.UserAuthenticatedDigest,ct);
  var inspection=await IsoInspector.InspectAsync(IsoPath,selected,ct);
  if(!DistributionAdapter.For(selected).DetectImage(selected,inspection))throw new SafetyException(inspection.Findings.FirstOrDefault()??"UnsupportedImage");
  image=verified;SetStatus("ImageReady");
 }
 private async Task DownloadAsync(CancellationToken ct) {
  image=null;InvalidatePlan();if(!DigestTrusted)throw new SafetyException("TrustedDigestRequired");Settings.Save();
  var selected=Release??throw new SafetyException("AdapterMismatch");
  string path=Path.Combine(Settings.DownloadDirectory,selected.Id+".iso");
  var progress=new Progress<DownloadProgress>(p=> {status="Ready";statusDetail=" "+string.Format(l["DownloadProgress"],p.Received/1048576d,p.BytesPerSecond/1048576d);Changed(nameof(Status));});
  await new DownloadEngine(Http).DownloadAsync(selected.DownloadUrl,path,Digest,selected.Id,TrustLevel.UserAuthenticatedDigest,progress,Settings.BandwidthMiB*1048576L,ct);
  IsoPath=path;await VerifyAsync(ct);
 }
 private async Task CreatePlanAsync(CancellationToken ct) {
  InvalidatePlan();var selected=Release??throw new SafetyException("AdapterMismatch");
  var verified=image??throw new SafetyException("ImageUnverified");
  var adapter=DistributionAdapter.For(selected);adapter.ValidateRelease(selected,false);
  adapter.ValidateHost(host??throw new SafetyException("UnsupportedHost"),selected);
  var target=disk??throw new SafetyException("SelectDisk");var source=partition??throw new SafetyException("SourceMissing");
  if(!long.TryParse(SizeGiB,NumberStyles.None,CultureInfo.InvariantCulture,out var gib)||gib<=0)throw new SafetyException("InvalidNumber");
  // Rehash and re-inspect before recording a plan. A previously verified path is not a trust anchor.
  verified=await ImageVerifier.VerifyAsync(verified.Path,verified.Sha256,selected.Id,verified.Trust,ct);
  if(!(await IsoInspector.InspectAsync(verified.Path,selected,ct)).Compatible)throw new SafetyException("UnsupportedImage");
  plan=Planner.Create(target,source.Id,checked(gib*1024*1024*1024),selected,verified);
  adapter.ValidateInstallationPlan(plan,selected);approvedFingerprint=PlanGuard.Fingerprint(plan);RefreshMaps();SetStatus("Ready");
 }
 private async Task SavePlanAsync(CancellationToken ct) {
  if(plan is null || !Approved || approvedFingerprint is null)throw new SafetyException("PlanRequired");
  var current=(await storage.DiscoverAsync(ct)).SingleOrDefault(d=>d.Number==plan.OriginalDisk.Number)??throw new SafetyException("DiskChanged");
  PlanGuard.Validate(plan,current,approvedFingerprint,false);
  await ImageVerifier.VerifyAsync(plan.Image.Path,plan.Image.Sha256,plan.ProfileId,plan.Image.Trust,ct);
  await store.SaveDryRunAsync(plan,ct);savedOperation=plan.OperationId;
  await store.TransitionAsync(plan.OperationId,OperationPhase.IMAGE_READY,"ImageVerified",ct);
  await store.TransitionAsync(plan.OperationId,OperationPhase.PLAN_APPROVED,"PlanApproved",ct);
  SetStatus("DryRunSaved");Stage=5;await InspectAsync(ct);
 }
 private async Task AdvanceAsync(CancellationToken ct) {
  if(Stage==1&&image is null)throw new SafetyException("ImageUnverified");
  if(Stage==3)await CreatePlanAsync(ct);
  if(Stage==4&&savedOperation is null)throw new SafetyException("PlanRequired");
  Stage++;
 }
 private async Task CancelAsync() {
  if(Busy){active?.Cancel();return;}
  await Run(async ct=> {if(savedOperation is Guid id)await store.TransitionAsync(id,OperationPhase.CANCELLED,"OperationCancelled",ct);InvalidatePlan();Stage=0;SetStatus("Cancelled");await InspectAsync(ct);});
 }
 private async Task InspectAsync(CancellationToken ct) {
  Operations.Clear();foreach(var record in await new RecoveryService(store).InspectAsync(ct))Operations.Add(string.Format(l["SavedOperation"],record.OperationId,l[record.Phase.ToString()],l[record.DryRun?"DryRun":"Unverified"]));
  if(Operations.Count==0)Operations.Add(l["NoOperations"]);
 }
 private async Task ExportAsync(CancellationToken ct) {
  var dialog=new SaveFileDialog {Filter="JSON (*.json)|*.json",FileName="orchester-diagnostics.json"};
  if(dialog.ShowDialog()!=true)return;
  await new RecoveryService(store).ExportRedactedAsync(dialog.FileName,host??throw new SafetyException("UnsupportedHost"),Disks.Select(d=>d.Value),ct);SetStatus("Exported");
 }
 private string DiskLabel(DiskSnapshot d)=>string.Format(l["DiskSummary"],d.Number,d.Name,d.Capacity/1073741824d,d.PartitionStyle,d.Health=="Healthy"?l["Healthy"]:l["Unknown"]);
 private string PartitionLabel(PartitionSnapshot p)=>string.Format(l["PartitionSummary"],p.Number,p.FileSystem,p.Length/1073741824d,p.ShrinkBytes is long limit?$"{limit/1073741824d:N1} GiB":l["Unknown"],l[p.Encryption==EncryptionState.Unknown?"UnknownEncryption":p.Encryption.ToString()]);
 private void RefreshInventory(){Inventory.Clear();foreach(var d in Disks){Inventory.Add(DiskLabel(d.Value));foreach(var p in d.Value.Partitions)Inventory.Add(PartitionLabel(p));}}
 private void RefreshText(){Changed(nameof(Status));Changed(nameof(StageTitle));Changed(nameof(HostText));RefreshInventory();RefreshMaps();}
 private void RefreshMaps() {
  CurrentMap.Clear();ProposedMap.Clear();Changes.Clear();if(disk is null)return;
  double width=500d/disk.Capacity;
  void Add(ObservableCollection<MapSegment> map,string label,long length,bool proposed=false){if(length>0)map.Add(new(label+" · "+(length/1073741824d).ToString("N1")+" GiB",Math.Max(1,length*width),proposed));}
  long end=0;
  foreach(var p in disk.Partitions.OrderBy(p=>p.Offset)) {
   Add(CurrentMap,l["Unallocated"],p.Offset-end);Add(ProposedMap,l["Unallocated"],p.Offset-end);
   string label=$"#{p.Number} {p.FileSystem}";Add(CurrentMap,label,p.Length);
   if(plan is not null&&p.Id==plan.SourcePartition){Add(ProposedMap,label,p.Length-plan.RequestedBytes-plan.StagingBytes);Add(ProposedMap,l["LinuxReserved"],plan.RequestedBytes,true);Add(ProposedMap,l["TemporaryStaging"],plan.StagingBytes,true);}else Add(ProposedMap,label,p.Length);
   end=p.Offset+p.Length;
  }
  Add(CurrentMap,l["Unallocated"],disk.Capacity-end);Add(ProposedMap,l["Unallocated"],disk.Capacity-end);
  if(plan is not null)foreach(var c in plan.Changes)Changes.Add(string.Format(l["ChangeSummary"],l[c.Kind],c.Offset/1048576d,c.Length/1073741824d));
 }
 private void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}
