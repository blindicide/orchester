using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orchester.Core;
namespace Orchester.Images;
public static class CatalogService {
 public static ImmutableArray<ReleaseProfile> LoadBuiltin() {
  using var source=typeof(CatalogService).Assembly.GetManifestResourceStream("Orchester.BuiltinCatalog")??throw new SafetyException("CatalogInvalid");
  using var bytes=new MemoryStream();source.CopyTo(bytes);return Parse(bytes.ToArray());
 }
 public static ImmutableArray<ReleaseProfile> LoadBundled(string path) => Parse(File.ReadAllBytes(path));
 // Updates require an out-of-band pinned publisher key. No downloaded keys or executable adapters.
 public static ImmutableArray<ReleaseProfile> VerifyUpdate(byte[] payload,byte[] signature,RSA pinnedPublisherKey) {
  if(!pinnedPublisherKey.VerifyData(payload,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pss)) throw new SafetyException("CatalogSignatureInvalid");
  return Parse(payload);
 }
 private sealed record Catalog(int SchemaVersion,ImmutableArray<ReleaseProfile> Releases);
 private static ImmutableArray<ReleaseProfile> Parse(byte[] bytes) {
  if(bytes.Length>1024*1024) throw new SafetyException("CatalogInvalid");
  var catalog=JsonSerializer.Deserialize<Catalog>(bytes,new JsonSerializerOptions { PropertyNameCaseInsensitive=true,Converters={new JsonStringEnumConverter()} }) ?? throw new SafetyException("CatalogInvalid");
  if(catalog.SchemaVersion!=1 || catalog.Releases.IsDefaultOrEmpty || catalog.Releases.Select(x=>x.Id).Distinct().Count()!=catalog.Releases.Length) throw new SafetyException("CatalogInvalid");
  foreach(var p in catalog.Releases) {
   if(!System.Text.RegularExpressions.Regex.IsMatch(p.Id,@"\A[a-z0-9]+(?:-[a-z0-9.]+)+\z") || p.Id.Length>100 || p.Architecture!="x86-64" || p.DownloadUrl.Scheme!="https" || p.ChecksumUrl.Scheme!="https" || p.MinimumStorageBytes<=0 || p.AdapterVersion!="0.1.0" || p.Modes.IsDefaultOrEmpty || !SafePath(p.KernelPath) || !SafePath(p.InitrdPath)) throw new SafetyException("CatalogInvalid");
  }
  return catalog.Releases;
 }
 private static bool SafePath(string path) => path.StartsWith('/') && !path.Contains("..") && !path.Contains('\\') && path.Length<256;
}
