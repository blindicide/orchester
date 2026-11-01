using System.Security.Cryptography;
using System.Text.Json;
using Orchester.Core;
namespace Orchester.Security;

public sealed record ReleaseUpdateManifest(int SchemaVersion, string Version, string Architecture, Uri DownloadUrl,
    string Sha256, string AuthenticodePublicKeySha256);
public sealed class AuthenticatedRelease {
    public ReleaseUpdateManifest Manifest { get; }
    internal AuthenticatedRelease(ReleaseUpdateManifest manifest) => Manifest=manifest;
}
public static class SignedRelease {
    public static AuthenticatedRelease Verify(byte[] exactPayload, byte[] signature, RSA pinnedReleaseKey, Version currentVersion) {
        if (pinnedReleaseKey.KeySize<2048 || exactPayload.Length>64*1024 || !pinnedReleaseKey.VerifyData(exactPayload,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pss))
            throw new SafetyException("UpdateSignatureInvalid");
        ReleaseUpdateManifest manifest;
        try {manifest=JsonSerializer.Deserialize<ReleaseUpdateManifest>(exactPayload)??throw new SafetyException("UpdateSignatureInvalid");}
        catch(JsonException){throw new SafetyException("UpdateSignatureInvalid");}
        if(manifest.SchemaVersion!=1 || !Version.TryParse(manifest.Version,out var version) || version<=currentVersion ||
            manifest.Architecture!="win-x64" || manifest.DownloadUrl is null || manifest.DownloadUrl.Scheme!="https" ||
            !string.IsNullOrEmpty(manifest.DownloadUrl.UserInfo) || !Digest(manifest.Sha256) || !Digest(manifest.AuthenticodePublicKeySha256))
            throw new SafetyException("UpdateRejected");
        return new(manifest);
    }
    private static bool Digest(string? text)=>text is {Length:64}&&text.All(Uri.IsHexDigit);
}
