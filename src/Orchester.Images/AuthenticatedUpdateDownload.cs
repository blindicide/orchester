using Orchester.Core;
using Orchester.Security;
namespace Orchester.Images;

public sealed class AuthenticatedUpdateDownload(HttpClient client) {
    public async Task<string> StageAsync(AuthenticatedRelease release,string privateDirectory,CancellationToken ct) {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        WorkspaceSafety.RejectReparsePoints(privateDirectory);Directory.CreateDirectory(privateDirectory);WorkspaceSafety.MakePrivate(privateDirectory,true);
        string path=WorkspaceSafety.ResolveChild(privateDirectory,"orchester-"+Version.Parse(release.Manifest.Version)+"-win-x64.exe");
        var image=await new DownloadEngine(client).DownloadAsync(release.Manifest.DownloadUrl,path,release.Manifest.Sha256,
            "orchester-update",TrustLevel.PublisherAuthenticated,null,0,ct);
        AuthenticodeVerifier.VerifyPublisher(image.Path,release.Manifest.AuthenticodePublicKeySha256);
        return image.Path; // No process launch, silent installation, or trust-on-first-use key enrollment.
    }
}
