using Orchester.Core;
namespace Orchester.Adapters;
public abstract class DistributionAdapter : IInstallationAdapter {
 public abstract string Id { get; } public string Version=>"0.1.0";
 public bool DetectImage(ReleaseProfile profile,ImageInspection inspection) => profile.Id.StartsWith(Id+"-",StringComparison.Ordinal) && inspection.Compatible;
 public virtual void ValidateRelease(ReleaseProfile profile,bool execution) {
  if(!profile.Id.StartsWith(Id+"-",StringComparison.Ordinal) || profile.AdapterVersion!=Version) throw new SafetyException("AdapterMismatch");
  if(execution && !profile.Validated) throw new SafetyException("ReleaseUnvalidated");
 }
 public void ValidateHost(HostSnapshot host,ReleaseProfile profile) {
  if(!host.SupportedWindows || !host.Is64Bit || !host.Uefi) throw new SafetyException("UnsupportedHost");
  if(host.SecureBoot is null || (host.SecureBoot==true && !profile.SecureBoot)) throw new SafetyException("SecureBootUnsupported");
 }
 public long CalculateSpaceRequirements(ReleaseProfile profile,long imageBytes) => checked(profile.MinimumStorageBytes+imageBytes+2L*1024*1024*1024);
 public void ValidateInstallationPlan(InstallationPlan plan,ReleaseProfile profile) {
  ValidateRelease(profile,plan.Mode==InstallationMode.Automated);
  if(plan.AdapterVersion!=Version || plan.ProfileId!=profile.Id || !profile.Modes.Contains(plan.Mode)) throw new SafetyException("AdapterMismatch");
 }
 public string PrepareInstaller(ReleaseProfile profile) => System.Text.Json.JsonSerializer.Serialize(InstallerRecipes.Create(profile));
 public string GenerateBootConfiguration(ReleaseProfile profile) => InstallerRecipes.GrubPreview(profile);
 public virtual string GenerateUnattendedConfiguration(ReleaseProfile profile) => throw new SafetyException("AutomationUnavailable");
 public string GetRecoveryInstructions()=>"InspectJournal";
 public static IInstallationAdapter For(ReleaseProfile profile) => profile.Id.Split('-')[0] switch {
  "ubuntu"=>new Ubuntu.UbuntuAdapter(), "debian"=>new Debian.DebianAdapter(), "mint"=>new LinuxMint.LinuxMintAdapter(), _=>throw new SafetyException("AdapterMismatch") };
}
