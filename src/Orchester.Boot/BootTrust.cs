using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Orchester.Core;
using Orchester.Security;
namespace Orchester.Boot;

[SupportedOSPlatform("windows")]
public static class BootTrust {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    private static extern uint GetFirmwareEnvironmentVariableW(string name,string guid,byte[] output,uint size);
    public static void VerifyFirmwareLoader(string path) {
        using var signer=AuthenticodeVerifier.LoadSigner(path);
        string pin=Convert.ToHexString(SHA256.HashData(signer.PublicKey.ExportSubjectPublicKeyInfo()));
        AuthenticodeVerifier.VerifyPublisher(path,pin); // Valid Authenticode digest and certificate chain.
        byte[] db=Read("db"), dbx=Read("dbx");
        var trusted=Certificates(db);var revoked=Certificates(dbx);
        using var chain=new X509Chain();chain.ChainPolicy.TrustMode=X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.DisableCertificateDownloads=true;chain.ChainPolicy.RevocationMode=X509RevocationMode.NoCheck; // Firmware uses dbx; do not substitute Windows CRL policy for its trust database.
        foreach(var cert in trusted)chain.ChainPolicy.CustomTrustStore.Add(cert);
        try {
            if(trusted.Count==0||!chain.Build(signer)||revoked.Any(c=>chain.ChainElements.Cast<X509ChainElement>().Any(e=>e.Certificate.RawData.SequenceEqual(c.RawData))))throw new SafetyException("SecureBootUnsupported");
            // Firmware SHA-256 revocations use the Authenticode image digest, not the whole-file digest.
            byte[] fullHash=PeImage.AuthenticodeSha256(path);
            if(Signatures(dbx,new Guid("C1C41626-504C-4092-ACA9-41F936934328")).Any(h=>h.SequenceEqual(fullHash)))throw new SafetyException("SecureBootUnsupported");
            var tbsRevocations=Signatures(dbx,new Guid("3BD2A492-96C0-4079-B420-FCF98EF103ED"));
            foreach(X509ChainElement item in chain.ChainElements) {
                var outer=new System.Formats.Asn1.AsnReader(item.Certificate.RawData,System.Formats.Asn1.AsnEncodingRules.DER).ReadSequence();
                byte[] tbsHash=SHA256.HashData(outer.ReadEncodedValue().Span);
                if(tbsRevocations.Any(r=>r.Length>=32&&r.AsSpan(0,32).SequenceEqual(tbsHash)))throw new SafetyException("SecureBootUnsupported");
            }
        } finally {foreach(var c in trusted)c.Dispose();foreach(var c in revoked)c.Dispose();}
    }
    public static void VerifySbat(string shim,string grub) {
        var output=new byte[65536];uint size=GetFirmwareEnvironmentVariableW("SbatLevelRT","{605DAB50-E046-4300-ABB6-3DD810DD8B23}",output,(uint)output.Length);
        if(size==0){if(Marshal.GetLastWin32Error()==203)return;throw new SafetyException("SecureBootUnsupported");}
        var policy=new Dictionary<string,long>(StringComparer.Ordinal);
        foreach(string line in System.Text.Encoding.UTF8.GetString(output,0,(int)size).TrimEnd('\0').Split('\n')){var fields=line.Trim().Split(',');if(fields.Length>=2&&long.TryParse(fields[1],out long generation))policy[fields[0]]=generation;}
        foreach(string file in new[]{shim,grub}) {
            string data=PeImage.SectionText(file,".sbat")??throw new SafetyException("SecureBootUnsupported");
            foreach(string line in data.Split('\n')){var fields=line.Trim().Split(',');if(fields.Length>=2&&fields[0]!="sbat"&&policy.TryGetValue(fields[0],out long revoked)&&(!long.TryParse(fields[1],out long generation)||generation<=revoked))throw new SafetyException("SecureBootUnsupported");}
        }
    }
    private static byte[] Read(string name) {
        var bytes=new byte[1024*1024];uint size=GetFirmwareEnvironmentVariableW(name,"{D719B2CB-3D3A-4596-A3BC-DAD00E67656F}",bytes,(uint)bytes.Length);
        if(size==0){if(name=="dbx"&&Marshal.GetLastWin32Error()==203)return [];throw new SafetyException("SecureBootUnsupported");}
        return bytes.AsSpan(0,(int)size).ToArray();
    }
    private static List<X509Certificate2> Certificates(byte[] bytes)=>Signatures(bytes,new("A5C059A1-94E4-4AA7-87B5-AB155C2BF072")).Select(x=>X509CertificateLoader.LoadCertificate(x)).ToList();
    private static List<byte[]> Signatures(byte[] bytes,Guid type) {
        var result=new List<byte[]>();
        for(int offset=0;offset<bytes.Length;) {
            if(bytes.Length-offset<28)throw new SafetyException("FirmwareMalformed");
            var current=new Guid(bytes.AsSpan(offset,16));int size=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset+16,4)));
            int header=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset+20,4))), signature=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset+24,4)));
            if(size<28||size>bytes.Length-offset||header>size-28||signature<16||(size-28-header)%signature!=0)throw new SafetyException("FirmwareMalformed");
            if(current==type)for(int p=offset+28+header;p<offset+size;p+=signature)result.Add(bytes.AsSpan(p+16,signature-16).ToArray());
            offset+=size;
        }
        return result;
    }
}
