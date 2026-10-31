using System.IO;
using System.Text.Json;
namespace Orchester.App;
public sealed record UserSettings {
 public string? Language { get; set; }
 public bool Dark { get; set; }
 public string DownloadDirectory { get; set; }=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads","Orchester");
 public string StagingDirectory { get; set; }=Path.Combine(App.DataRoot,"Staging");
 public int BandwidthMiB { get; set; }
 public bool Verbose { get; set; }
 public bool CheckUpdates { get; set; }
 public bool RetainImages { get; set; }=true;
 public string PreferredMethod { get; set; }="Guided";
 private static string FilePath=>Path.Combine(App.DataRoot,"settings.json");
 public static UserSettings Load() { try { return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new(); } catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException) { return new(); } }
 public void Save() {
  if(BandwidthMiB<0 || BandwidthMiB>1024*1024 || !Path.IsPathFullyQualified(DownloadDirectory) || !Path.IsPathFullyQualified(StagingDirectory)) throw new Orchester.Core.SafetyException("InvalidNumber");
  Directory.CreateDirectory(App.DataRoot); File.WriteAllText(FilePath+".tmp",JsonSerializer.Serialize(this)); File.Move(FilePath+".tmp",FilePath,true);
 }
}
