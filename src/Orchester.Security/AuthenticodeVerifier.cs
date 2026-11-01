using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Orchester.Core;
namespace Orchester.Security;

public static class AuthenticodeVerifier {
    [StructLayout(LayoutKind.Sequential)] private struct TrustFile {public uint Size;public IntPtr Path;public IntPtr File;public IntPtr KnownSubject;}
    [StructLayout(LayoutKind.Sequential)] private struct TrustData {
        public uint Size;public IntPtr Policy;public IntPtr Sip;public uint Ui;public uint Revocation;public uint Union;
        public IntPtr File;public uint StateAction;public IntPtr State;public IntPtr Url;public uint Flags;public uint Context;public IntPtr SignatureSettings;
    }
    [DllImport("wintrust.dll",ExactSpelling=true,SetLastError=true)]
    private static extern int WinVerifyTrust(IntPtr window,ref Guid action,ref TrustData data);
    [SupportedOSPlatform("windows")]
    public static void VerifyPublisher(string path,string expectedPublicKeySha256) {
        if(expectedPublicKeySha256.Length!=64||!expectedPublicKeySha256.All(Uri.IsHexDigit))throw new SafetyException("ExecutableSignatureInvalid");
        WorkspaceSafety.RejectReparsePoints(path);
        Guid action=new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        IntPtr name=Marshal.StringToCoTaskMemUni(Path.GetFullPath(path));
        IntPtr info=Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        var data=new TrustData {Size=(uint)Marshal.SizeOf<TrustData>(),Ui=2,Revocation=1,Union=1,File=info,StateAction=1,Flags=0x80};
        try {
            Marshal.StructureToPtr(new TrustFile {Size=(uint)Marshal.SizeOf<TrustFile>(),Path=name},info,false);
            if(WinVerifyTrust(new IntPtr(-1),ref action,ref data)!=0)throw new SafetyException("ExecutableSignatureInvalid");
            using var signed=X509Certificate.CreateFromSignedFile(path);
            using var certificate=X509CertificateLoader.LoadCertificate(signed.Export(X509ContentType.Cert));
            byte[] digest=SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo());
            if(!CryptographicOperations.FixedTimeEquals(digest,Convert.FromHexString(expectedPublicKeySha256)))throw new SafetyException("ExecutableSignatureInvalid");
        } catch(CryptographicException){throw new SafetyException("ExecutableSignatureInvalid");}
        finally {data.StateAction=2;WinVerifyTrust(new IntPtr(-1),ref action,ref data);Marshal.FreeHGlobal(info);Marshal.FreeCoTaskMem(name);}
    }
}
