using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
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
 public async void Execute(object? parameter)=>await ExecuteAsync();
 public Task ExecuteAsync()=>CanExecute(null)?action():Task.CompletedTask;
 public event EventHandler? CanExecuteChanged { add=>CommandManager.RequerySuggested+=value; remove=>CommandManager.RequerySuggested-=value; }
}
public sealed record Choice<T>(T Value,string Label);
public sealed record MapSegment(string Label,double Width,bool Proposed);
public sealed class MainViewModel : INotifyPropertyChanged {
 private readonly LanguageManager l=LanguageManager.Current;
 private readonly OperationStore store;
 private readonly IStorageDiscovery storage;
 private readonly Func<HostSnapshot> inspectHost;
 private static readonly HttpClient Http=new(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=Timeout.InfiniteTimeSpan };
 private CancellationTokenSource? active;
 private InstallationPlan? plan;
 private string? approvedFingerprint;
 private Guid? savedOperation;
 private VerifiedImage? image;
 private HostSnapshot? host;
 private int stage,section;
 private bool busy,approved,digestTrusted,relabeling,elevatedStorage,nativePreparing,storageConsent,backupConfirmed,linuxBootConfirmed;
 private ImmutableArray<RecoveryRecord> operationRecords=[];
 private string status="Ready",statusDetail="",distribution="",isoPath="",digest="",size="40";
 private string hdKernelPath="",hdKernelDigest="",hdInitrdPath="",hdInitrdDigest="";
 private NativeOperationSummary? selectedOperation;
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
 public ObservableCollection<NativeOperationSummary> NativeOperations {get;}=[];
 public NativeOperationSummary? SelectedOperation {get=>selectedOperation;set{selectedOperation=value;Changed();Changed(nameof(OperationStatus));CommandManager.InvalidateRequerySuggested();}}
 public string OperationStatus=>SelectedOperation is null?"":(Catalog.FirstOrDefault(p=>p.Id==SelectedOperation.ProfileId)?.Distribution??SelectedOperation.ProfileId)+" · "+l[SelectedOperation.Phase.ToString()];
 public Visibility HdMediaVisibility=>Release?.Id.StartsWith("debian-",StringComparison.Ordinal)==true?Visibility.Visible:Visibility.Collapsed;
 public string HdKernelPath {get=>hdKernelPath;set{hdKernelPath=value;InvalidatePlan();Changed();}}
 public string HdKernelDigest {get=>hdKernelDigest;set{hdKernelDigest=value.Trim();InvalidatePlan();Changed();}}
 public string HdInitrdPath {get=>hdInitrdPath;set{hdInitrdPath=value;InvalidatePlan();Changed();}}
 public string HdInitrdDigest {get=>hdInitrdDigest;set{hdInitrdDigest=value.Trim();InvalidatePlan();Changed();}}
 public string ReleaseStatus=>l[Release?.Validated==true?"QualifiedRelease":"PendingRelease"];
 public bool StorageConsent {get=>storageConsent;set{storageConsent=value;Changed();CommandManager.InvalidateRequerySuggested();}}
 public bool BackupConfirmed {get=>backupConfirmed;set{backupConfirmed=value;Changed();CommandManager.InvalidateRequerySuggested();}}
 public bool LinuxBootConfirmed {get=>linuxBootConfirmed;set{linuxBootConfirmed=value;Changed();CommandManager.InvalidateRequerySuggested();}}
 public ObservableCollection<string> FirmwareEntries { get; }=[];
 public ObservableCollection<string> Inventory { get; }=[];
 public string[] Languages { get; }=["en-US","ru-RU"];
 public string Language { get=>l.Language; set { l.SetLanguage(value); Settings.Language=l.Language; Changed(); RefreshText(); } }
 public System.Windows.Markup.XmlLanguage InterfaceXmlLanguage=>System.Windows.Markup.XmlLanguage.GetLanguage(Language);
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
 public ReleaseProfile? Release { get=>release; set {release=value;image=null;InvalidatePlan();Changed();Changed(nameof(DownloadUrl));Changed(nameof(ChecksumUrl));Changed(nameof(HdMediaVisibility));Changed(nameof(ReleaseStatus));} }
 public string DownloadUrl=>Release?.DownloadUrl.ToString()??"";
 public string ChecksumUrl=>Release?.ChecksumUrl.ToString()??"";
 public string IsoPath { get=>isoPath;set {isoPath=value;image=null;InvalidatePlan();Changed();} }
 public string Digest { get=>digest;set {digest=value.Trim();image=null;InvalidatePlan();Changed();} }
 public bool DigestTrusted { get=>digestTrusted;set {digestTrusted=value;image=null;InvalidatePlan();Changed();} }
 public Choice<DiskSnapshot>? SelectedDisk { get=>Disks.FirstOrDefault(d=>d.Value==disk);set {if(relabeling)return;disk=value?.Value;Partitions.Clear();if(disk is not null)foreach(var p in disk.Partitions)Partitions.Add(new(p,PartitionLabel(p)));SelectedPartition=Partitions.FirstOrDefault(p=>p.Value.FileSystem=="NTFS"&&!p.Value.Protected);InvalidatePlan();RefreshMaps();Changed();} }
 public Choice<PartitionSnapshot>? SelectedPartition { get=>Partitions.FirstOrDefault(p=>p.Value==partition);set {if(relabeling)return;partition=value?.Value;InvalidatePlan();Changed();} }
 public string SizeGiB { get=>size;set {size=value;InvalidatePlan();Changed();} }
 public bool Approved { get=>approved;set {approved=value;Changed();CommandManager.InvalidateRequerySuggested();} }
 public ICommand NativePrepareCommand {get;}
 public ICommand NativeBootCommand {get;}
 public ICommand ListNativeCommand {get;}
 public ICommand CleanupNativeCommand {get;}
 public ICommand CompleteNativeCommand {get;}
 public ICommand AbortRestartCommand {get;}
 public ICommand RestartCommand {get;}
 public ICommand ElevatedStorageCommand { get; }
 public ICommand PrepareBundleCommand { get; }
 public ICommand VerifyBundleCommand { get; }
 public ICommand FirmwareCommand { get; }
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
 public MainViewModel(IStorageDiscovery? storageDiscovery=null,Func<HostSnapshot>? hostInspector=null,OperationStore? operationStore=null) {
  inspectHost=hostInspector??HostInspector.Inspect;
  storage=storageDiscovery??new WindowsStorageDiscovery(inspectHost().SupportedWindows);
  store=operationStore??new OperationStore(Path.Combine(App.DataRoot,"Operations"));
  Catalog=CatalogService.LoadBundled(Path.Combine(AppContext.BaseDirectory,"catalog.json"));
  foreach(var d in Catalog.Select(p=>p.Distribution).Distinct()) Distributions.Add(d);
  Distribution=Distributions[0];
  NativePrepareCommand=new AsyncCommand(()=>Run(PrepareNativeAsync),()=>Idle&&plan is not null&&savedOperation is not null&&StorageConsent&&BackupConfirmed);
  NativeBootCommand=new AsyncCommand(()=>Run(ct=>NativeActionAsync(DiagnosticCommand.RequestInstallerBoot,ct)),()=>Idle);
  ListNativeCommand=new AsyncCommand(()=>Run(ListNativeAsync),()=>Idle);
  CleanupNativeCommand=new AsyncCommand(()=>Run(ct=>NativeActionAsync(DiagnosticCommand.CleanupOperation,ct)),()=>Idle&&SelectedOperation is not null);
  CompleteNativeCommand=new AsyncCommand(()=>Run(ct=>NativeActionAsync(DiagnosticCommand.CompleteInstallation,ct)),()=>Idle&&SelectedOperation is not null&&LinuxBootConfirmed);
  AbortRestartCommand=new AsyncCommand(()=>Run(_=>{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","shutdown.exe")){UseShellExecute=false,ArgumentList={"/a"}});SetStatus("RestartCancelled");return Task.CompletedTask;}),()=>Idle);
  RestartCommand=new AsyncCommand(()=>Run(_=>{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","shutdown.exe")){UseShellExecute=false,ArgumentList={"/r","/t","15"}});SetStatus("RestartScheduled");return Task.CompletedTask;}),()=>Idle&&SelectedOperation?.Phase==OperationPhase.REBOOT_REQUESTED);
  ElevatedStorageCommand=new AsyncCommand(()=>Run(InspectElevatedStorageAsync),()=>Idle);
  PrepareBundleCommand=new AsyncCommand(()=>Run(PrepareBundleAsync),()=>Idle&&plan is not null&&savedOperation is not null);
  VerifyBundleCommand=new AsyncCommand(()=>Run(VerifyBundleAsync),()=>Idle&&plan is not null&&savedOperation is not null);
  FirmwareCommand=new AsyncCommand(()=>Run(InspectFirmwareAsync),()=>Idle);
  RefreshCommand=new AsyncCommand(()=>Run(RefreshAsync),()=>Idle);
  VerifyCommand=new AsyncCommand(()=>Run(VerifyAsync),()=>Idle);
  BrowseCommand=new AsyncCommand(()=> {var dialog=new OpenFileDialog {Filter="ISO (*.iso)|*.iso",CheckFileExists=true};if(dialog.ShowDialog()==true)IsoPath=dialog.FileName;return Task.CompletedTask;},()=>Idle);
  DownloadCommand=new AsyncCommand(()=>Run(DownloadAsync),()=>Idle);
  PlanCommand=new AsyncCommand(()=>Run(CreatePlanAsync),()=>Idle);
  SavePlanCommand=new AsyncCommand(()=>Run(SavePlanAsync),()=>Idle&&Approved&&plan is not null&&savedOperation is null);
  BackCommand=new AsyncCommand(()=> {Stage--;return Task.CompletedTask;},()=>Idle&&Stage>0);
  NextCommand=new AsyncCommand(()=>Run(AdvanceAsync),()=>Idle&&Stage<6);
  CancelCommand=new AsyncCommand(CancelAsync,()=>!nativePreparing&&(Busy || savedOperation is not null));
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
  elevatedStorage=false;InvalidatePlan();host=inspectHost();Changed(nameof(HostText));
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
  plan=NativeLayout.ForExecution(Planner.Create(target,source.Id,checked(gib*1024*1024*1024),selected,verified));
  if(selected.Id.StartsWith("debian-",StringComparison.Ordinal)) {
   if(string.IsNullOrWhiteSpace(HdKernelPath)||string.IsNullOrWhiteSpace(HdInitrdPath))throw new SafetyException("HdMediaRequired");
   var kernel=await ImageVerifier.VerifyAsync(HdKernelPath,HdKernelDigest,selected.Id,TrustLevel.UserAuthenticatedDigest,ct);
   var initrd=await ImageVerifier.VerifyAsync(HdInitrdPath,HdInitrdDigest,selected.Id,TrustLevel.UserAuthenticatedDigest,ct);
   plan=plan with {AuxiliaryImages=ImmutableSortedDictionary<string,VerifiedImage>.Empty.Add("hd-kernel",kernel).Add("hd-initrd",initrd)};
  }
  adapter.ValidateInstallationPlan(plan,selected);approvedFingerprint=PlanGuard.Fingerprint(plan);RefreshMaps();SetStatus("Ready");
 }
 private async Task SavePlanAsync(CancellationToken ct) {
  if(plan is null || !Approved || approvedFingerprint is null)throw new SafetyException("PlanRequired");
  if(elevatedStorage) {
   var checkedPlan=await ElevatedDiagnostics.InspectAsync(DiagnosticCommand.ValidatePlan,ct,plan,approvedFingerprint);
   if(!checkedPlan.Success)throw new SafetyException(checkedPlan.ErrorCode??"UnauthorizedRequest");
  } else {
   var current=(await storage.DiscoverAsync(ct)).SingleOrDefault(d=>d.Number==plan.OriginalDisk.Number)??throw new SafetyException("DiskChanged");
   PlanGuard.Validate(plan,current,approvedFingerprint,false);
  }
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
  operationRecords=await new RecoveryService(store).InspectAsync(ct);RenderOperations();
 }
 private void RenderOperations() {
  Operations.Clear();foreach(var record in operationRecords)Operations.Add(string.Format(l["SavedOperation"],record.OperationId,l[record.Phase.ToString()],l[record.DryRun?"DryRun":"Unverified"]));
  if(Operations.Count==0)Operations.Add(l["NoOperations"]);
 }
 private async Task PrepareNativeAsync(CancellationToken ct) {
  var current=plan??throw new SafetyException("PlanRequired");
  if(!StorageConsent||!BackupConfirmed)throw new SafetyException("AuthorizationRequired");
  nativePreparing=true;CommandManager.InvalidateRequerySuggested();
  try {
   var progress=new Progress<OperationProgress>(update=>SetStatus(update.Phase));
   var result=await ElevatedDiagnostics.SendAsync(new(1,current.OperationId,"",DiagnosticCommand.PrepareInstallation,null,null,new(current,PlanGuard.Fingerprint(current),StorageConsent,BackupConfirmed)),ct,progress);
   if(!result.Success)throw new SafetyException(result.ErrorCode??"UnexpectedError");
   var summary=result.Data?.Deserialize<NativeOperationSummary>()??throw new SafetyException("JournalIncomplete");
   NativeOperations.Add(summary);SelectedOperation=summary;Stage=6;SetStatus("NativePrepared");
  } finally {nativePreparing=false;CommandManager.InvalidateRequerySuggested();}
 }
 private async Task ListNativeAsync(CancellationToken ct) {
  var result=await ElevatedDiagnostics.SendAsync(new(1,Guid.NewGuid(),"",DiagnosticCommand.ListOperations,null,null),ct);
  if(!result.Success)throw new SafetyException(result.ErrorCode??"UnexpectedError");
  var rows=result.Data?.Deserialize<ImmutableArray<NativeOperationSummary>>()??[];
  NativeOperations.Clear();foreach(var row in rows)NativeOperations.Add(row);SelectedOperation=NativeOperations.FirstOrDefault();Changed(nameof(SelectedOperation));
  SetStatus("NativeOperationsLoaded");
 }
 private async Task NativeActionAsync(DiagnosticCommand command,CancellationToken ct) {
  var selected=SelectedOperation??throw new SafetyException("PlanRequired");
  nativePreparing=true;CommandManager.InvalidateRequerySuggested();
  try {
   var result=await ElevatedDiagnostics.SendAsync(new(1,selected.OperationId,"",command,null,null,null,LinuxBootConfirmed),ct);
   if(!result.Success)throw new SafetyException(result.ErrorCode??"UnexpectedError");
   if(command==DiagnosticCommand.RequestInstallerBoot){SelectedOperation=selected with {Phase=OperationPhase.REBOOT_REQUESTED};SetStatus("OneTimeBootReady");}
   else if(command==DiagnosticCommand.CompleteInstallation)SetStatus("InstallationConfirmed");
   else SetStatus("CleanupFinished");
   Changed(nameof(SelectedOperation));CommandManager.InvalidateRequerySuggested();
  } finally {nativePreparing=false;CommandManager.InvalidateRequerySuggested();}
 }
 private async Task InspectElevatedStorageAsync(CancellationToken ct) {
  var result=await ElevatedDiagnostics.InspectAsync(DiagnosticCommand.InspectStorage,ct);
  if(!result.Success)throw new SafetyException(result.ErrorCode??"DiscoveryFailed");
  var disks=result.Data?.Deserialize<ImmutableArray<DiskSnapshot>>()??throw new SafetyException("DiscoveryFailed");
  InvalidatePlan();Disks.Clear();foreach(var d in disks)Disks.Add(new(d,DiskLabel(d)));
  SelectedDisk=Disks.FirstOrDefault();elevatedStorage=true;RefreshInventory();SetStatus("StorageInspected");
 }
 private async Task PrepareBundleAsync(CancellationToken ct) {
  var current=plan??throw new SafetyException("PlanRequired");
  var profile=Release??throw new SafetyException("AdapterMismatch");
  if(!Path.IsPathFullyQualified(Settings.StagingDirectory))throw new SafetyException("UnsafePath");
  await new StagingWorkspace().PrepareAsync(current,profile,InstallerRecipes.Create(profile),Settings.StagingDirectory,false,ct);
  SetStatus("BundlePrepared");
 }
 private async Task VerifyBundleAsync(CancellationToken ct) {
  var current=plan??throw new SafetyException("PlanRequired");
  await new StagingWorkspace().VerifyAsync(Path.Combine(Settings.StagingDirectory,current.OperationId.ToString("D")),current,Release??throw new SafetyException("AdapterMismatch"),ct);
  SetStatus("BundleVerified");
 }
 private async Task InspectFirmwareAsync(CancellationToken ct) {
  var response=await ElevatedDiagnostics.InspectAsync(DiagnosticCommand.InspectFirmware,ct);
  if(!response.Success)throw new SafetyException(response.ErrorCode??"FirmwareUnavailable");
  var snapshot=response.Data?.Deserialize<FirmwareSnapshot>()??throw new SafetyException("FirmwareMalformed");
  FirmwareEntries.Clear();
  foreach(var entry in snapshot.Entries)FirmwareEntries.Add($"Boot{entry.Number:X4} · {entry.Description} · {entry.EfiPath}");
  SetStatus("FirmwareInspected");
 }
 private async Task ExportAsync(CancellationToken ct) {
  var dialog=new SaveFileDialog {Filter="JSON (*.json)|*.json",FileName="orchester-diagnostics.json"};
  if(dialog.ShowDialog()!=true)return;
  await new RecoveryService(store).ExportRedactedAsync(dialog.FileName,host??throw new SafetyException("UnsupportedHost"),Disks.Select(d=>d.Value),ct);SetStatus("Exported");
 }
 private string DiskLabel(DiskSnapshot d)=>string.Format(l["DiskSummary"],d.Number,d.Name,d.Capacity/1073741824d,d.PartitionStyle,d.Health=="Healthy"?l["Healthy"]:l["Unknown"]);
 private string PartitionPurpose(PartitionSnapshot p)=>l[p.Type.ToUpperInvariant() switch {
  "{C12A7328-F81F-11D2-BA4B-00A0C93EC93B}"=>"EfiPartition",
  "{DE94BBA4-06D1-4D40-A16A-BFD50179D6AC}"=>"RecoveryPartition",
  "{E3C9E316-0B5C-4DB8-817D-F92DF00215AE}"=>"ReservedPartition",
  "{0FC63DAF-8483-4772-8E79-3D69D8477DE4}"=>"LinuxPartition",
  "{0657FD6D-A4AB-43C4-84E5-0933C84B4F4F}"=>"LinuxPartition",
  _=>p.IsBoot?"WindowsPartition":"OtherPartition" }];
 private string PartitionLabel(PartitionSnapshot p)=>string.Format(l["PartitionSummary"],p.Number,PartitionPurpose(p)+" / "+p.FileSystem,p.Length/1073741824d,p.ShrinkBytes is long limit?$"{limit/1073741824d:N1} GiB":l["Unknown"],l[p.Encryption==EncryptionState.Unknown?"UnknownEncryption":p.Encryption.ToString()]);
 private void RefreshInventory(){Inventory.Clear();foreach(var d in Disks){Inventory.Add(DiskLabel(d.Value));foreach(var p in d.Value.Partitions)Inventory.Add(PartitionLabel(p));}}
 private void RefreshText(){
  relabeling=true;
  try {
   var disks=Disks.Select(d=>d.Value).ToArray();Disks.Clear();foreach(var d in disks)Disks.Add(new(d,DiskLabel(d)));
   var partitions=Partitions.Select(p=>p.Value).ToArray();Partitions.Clear();foreach(var p in partitions)Partitions.Add(new(p,PartitionLabel(p)));
  } finally {relabeling=false;}
  Changed(nameof(SelectedDisk));Changed(nameof(SelectedPartition));RenderOperations();
  Changed(nameof(ReleaseStatus));Changed(nameof(OperationStatus));Changed(nameof(InterfaceXmlLanguage));Changed(nameof(Status));Changed(nameof(StageTitle));Changed(nameof(HostText));RefreshInventory();RefreshMaps();}
 private void RefreshMaps() {
  CurrentMap.Clear();ProposedMap.Clear();Changes.Clear();if(disk is null)return;
  double width=500d/disk.Capacity;
  void Add(ObservableCollection<MapSegment> map,string label,long length,bool proposed=false){if(length>0)map.Add(new(label+" · "+(length/1073741824d).ToString("N1")+" GiB",Math.Max(1,length*width),proposed));}
  long end=0;
  foreach(var p in disk.Partitions.OrderBy(p=>p.Offset)) {
   Add(CurrentMap,l["Unallocated"],p.Offset-end);Add(ProposedMap,l["Unallocated"],p.Offset-end);
   string label=$"#{p.Number} {PartitionPurpose(p)} {p.FileSystem}";Add(CurrentMap,label,p.Length);
   if(plan is not null&&p.Id==plan.SourcePartition){Add(ProposedMap,label,p.Length-plan.RequestedBytes-plan.StagingBytes);Add(ProposedMap,l["LinuxReserved"],plan.RequestedBytes,true);Add(ProposedMap,l["InstallerEfi"],NativeLayout.EfiBytes,true);Add(ProposedMap,l["InstallerData"],plan.StagingBytes-NativeLayout.EfiBytes,true);}else Add(ProposedMap,label,p.Length);
   end=p.Offset+p.Length;
  }
  Add(CurrentMap,l["Unallocated"],disk.Capacity-end);Add(ProposedMap,l["Unallocated"],disk.Capacity-end);
  if(plan is not null)foreach(var c in plan.Changes)Changes.Add(string.Format(l["ChangeSummary"],l[c.Kind],c.Offset/1048576d,c.Length/1073741824d));
 }
 private void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}
