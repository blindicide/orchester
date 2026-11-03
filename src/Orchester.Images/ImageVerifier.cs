using System.Security.Cryptography;
using Orchester.Core;
namespace Orchester.Images;
public static class ImageVerifier {
 public static async Task<VerifiedImage> VerifyAsync(string path,string expectedDigest,string profileId,TrustLevel trust,CancellationToken ct) {
  if (trust==TrustLevel.Unverified || expectedDigest.Length!=64 || !expectedDigest.All(Uri.IsHexDigit)) throw new SafetyException("TrustedDigestRequired");
  using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan);
  var digest=await SHA256.HashDataAsync(file,ct);
  if(!CryptographicOperations.FixedTimeEquals(digest,Convert.FromHexString(expectedDigest))) throw new SafetyException("ChecksumMismatch");
  return new(Path.GetFullPath(path),Convert.ToHexString(digest).ToLowerInvariant(),file.Length,trust,profileId,DateTimeOffset.UtcNow);
 }
}
