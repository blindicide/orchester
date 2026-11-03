using System.Text.RegularExpressions;
using Orchester.Core;
namespace Orchester.Images;

public sealed record ChecksumReference(string Sha256,Uri MetadataUrl,bool AuthenticatedPublisherOrigin);
public static class PublisherChecksums {
    public static async Task<ChecksumReference> FetchAsync(ReleaseProfile profile,CancellationToken ct) {
        using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(45),MaxResponseContentBufferSize=1024*1024};
        Uri address=profile.ChecksumUrl;
        for(int redirect=0;redirect<6;redirect++) {
            if(address.Scheme!="https"||!string.IsNullOrEmpty(address.UserInfo))throw new SafetyException("HttpsRequired");
            using var response=await http.GetAsync(address,HttpCompletionOption.ResponseContentRead,ct);
            if((int)response.StatusCode is >=300 and <400) {address=response.Headers.Location is {IsAbsoluteUri:true} absolute?absolute:new Uri(address,response.Headers.Location??throw new SafetyException("ChecksumMetadataInvalid"));continue;}
            response.EnsureSuccessStatusCode();string text=await response.Content.ReadAsStringAsync(ct);
            string file=Uri.UnescapeDataString(profile.DownloadUrl.Segments[^1]);var hashes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string line in text.Split('\n')) {
                var match=Regex.Match(line,@"\A([A-Fa-f0-9]{64})\s+\*?(.+?)\s*\z");
                if(match.Success&&match.Groups[2].Value.TrimStart('.','/')==file)hashes.Add(match.Groups[1].Value);
            }
            if(hashes.Count!=1)throw new SafetyException("ChecksumMetadataInvalid");
            bool publisher=Official(profile.ChecksumUrl.Host)&&Official(address.Host);
            return new(hashes.Single().ToLowerInvariant(),address,publisher);
        }
        throw new SafetyException("ChecksumMetadataInvalid");
    }
    private static bool Official(string host)=>host.Equals("ubuntu.com",StringComparison.OrdinalIgnoreCase)||host.EndsWith(".ubuntu.com",StringComparison.OrdinalIgnoreCase)||host.Equals("debian.org",StringComparison.OrdinalIgnoreCase)||host.EndsWith(".debian.org",StringComparison.OrdinalIgnoreCase);
}
