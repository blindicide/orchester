using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Orchester.Core;
namespace Orchester.Images;
public sealed record DownloadProgress(long Received,long? Total,double BytesPerSecond);
public sealed class DownloadEngine(HttpClient client) {
 public async Task<VerifiedImage> DownloadAsync(Uri uri,string destination,string digest,string profileId,TrustLevel trust,IProgress<DownloadProgress>? progress,long bandwidthBytesPerSecond,CancellationToken ct) {
  if(uri.Scheme!=Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) throw new SafetyException("HttpsRequired");
  if(digest.Length!=64 || !digest.All(Uri.IsHexDigit) || trust==TrustLevel.Unverified) throw new SafetyException("TrustedDigestRequired");
  if(bandwidthBytesPerSecond<0) throw new ArgumentOutOfRangeException(nameof(bandwidthBytesPerSecond));
  var full=Path.GetFullPath(destination); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
  if(File.Exists(full)) {
   try { return await ImageVerifier.VerifyAsync(full,digest,profileId,trust,ct); }
   catch(SafetyException e) when(e.Code=="ChecksumMismatch") { throw new SafetyException("DestinationExists"); }
  }
  // A digest-specific partial file prevents combining downloads of different releases.
  var partial=full+"."+digest.ToLowerInvariant()+".partial";
  using var output=new FileStream(partial,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1024*1024,FileOptions.Asynchronous);
  var offset=output.Length;
  using var request=new HttpRequestMessage(HttpMethod.Get,uri);
  if(offset>0) request.Headers.Range=new RangeHeaderValue(offset,null);
  using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
  if(response.RequestMessage?.RequestUri?.Scheme!=Uri.UriSchemeHttps) throw new SafetyException("HttpsRequired");
  if(response.StatusCode==HttpStatusCode.RequestedRangeNotSatisfiable && offset>0) {
   await output.DisposeAsync();
   try {
    var complete=await ImageVerifier.VerifyAsync(partial,digest,profileId,trust,ct);
    File.Move(partial,full,false);return complete with {Path=full};
   } catch(SafetyException e) when(e.Code=="ChecksumMismatch") {File.Delete(partial);throw;}
  }
  response.EnsureSuccessStatusCode();
  if(response.StatusCode==HttpStatusCode.PartialContent) {
   if(response.Content.Headers.ContentRange?.From!=offset || response.Content.Headers.ContentRange?.Unit!="bytes") throw new SafetyException("InvalidResume");
  } else { output.SetLength(0); offset=0; }
  output.Position=offset;
  var total=response.Content.Headers.ContentRange?.Length ?? (response.Content.Headers.ContentLength is long len ? checked(len+offset) : (long?)null);
  using var input=await response.Content.ReadAsStreamAsync(ct);
  var buffer=new byte[1024*1024]; long received=offset; var watch=Stopwatch.StartNew(); int count;
  while((count=await input.ReadAsync(buffer,ct))>0) {
   await output.WriteAsync(buffer.AsMemory(0,count),ct); received=checked(received+count);
   if(total is long expected && received>expected) throw new SafetyException("DownloadLengthMismatch");
   progress?.Report(new(received,total,(received-offset)/Math.Max(.001,watch.Elapsed.TotalSeconds)));
   if(bandwidthBytesPerSecond>0) {
    var delay=TimeSpan.FromSeconds((received-offset)/(double)bandwidthBytesPerSecond)-watch.Elapsed;
    if(delay>TimeSpan.Zero) await Task.Delay(delay,ct);
   }
  }
  if(total is long size && received!=size) throw new SafetyException("DownloadLengthMismatch");
  await output.FlushAsync(ct); output.Flush(true); await output.DisposeAsync();
  VerifiedImage image;
  try { image=await ImageVerifier.VerifyAsync(partial,digest,profileId,trust,ct); }
  catch(SafetyException e) when(e.Code=="ChecksumMismatch") { File.Delete(partial); throw; }
  File.Move(partial,full,false); return image with { Path=full };
 }
}
