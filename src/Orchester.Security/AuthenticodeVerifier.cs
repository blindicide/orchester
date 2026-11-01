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
            using var certificate=LoadSigner(path);
            byte[] digest=SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo());
            if(!CryptographicOperations.FixedTimeEquals(digest,Convert.FromHexString(expectedPublicKeySha256)))throw new SafetyException("ExecutableSignatureInvalid");
        } catch(CryptographicException){throw new SafetyException("ExecutableSignatureInvalid");}
        finally {data.StateAction=2;WinVerifyTrust(new IntPtr(-1),ref action,ref data);Marshal.FreeHGlobal(info);Marshal.FreeCoTaskMem(name);}
    }
    [StructLayout(LayoutKind.Sequential)] private struct CertificateContext {public uint Encoding;public IntPtr Encoded;public uint Length;}
    [DllImport("crypt32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptQueryObject(uint objectType,string path,uint contentFlags,uint formatFlags,uint flags,out uint encoding,out uint content,out uint format,out IntPtr store,out IntPtr message,out IntPtr context);
    [DllImport("crypt32.dll",SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptMsgGetParam(IntPtr message,uint parameter,uint index,IntPtr data,ref uint length);
    [DllImport("crypt32.dll",SetLastError=true)] private static extern IntPtr CertFindCertificateInStore(IntPtr store,uint encoding,uint flags,uint findType,IntPtr findData,IntPtr previous);
    [DllImport("crypt32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CertFreeCertificateContext(IntPtr certificate);
    [DllImport("crypt32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CertCloseStore(IntPtr store,uint flags);
    [DllImport("crypt32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptMsgClose(IntPtr message);
    [SupportedOSPlatform("windows")]
    private static X509Certificate2 LoadSigner(string path) {
        IntPtr store=IntPtr.Zero,message=IntPtr.Zero,certificate=IntPtr.Zero,info=IntPtr.Zero;
        try {
            // Read embedded PKCS#7 signer metadata; WinVerifyTrust above validates its signature and chain.
            if(!CryptQueryObject(1,path,1u<<10,2,0,out _,out _,out _,out store,out message,out _))throw new SafetyException("ExecutableSignatureInvalid");
            uint length=0;
            if(!CryptMsgGetParam(message,7,0,IntPtr.Zero,ref length)||length==0||length>1024*1024)throw new SafetyException("ExecutableSignatureInvalid");
            info=Marshal.AllocHGlobal((int)length);
            if(!CryptMsgGetParam(message,7,0,info,ref length))throw new SafetyException("ExecutableSignatureInvalid");
            certificate=CertFindCertificateInStore(store,0x10001,0,0xB0000,info,IntPtr.Zero);
            if(certificate==IntPtr.Zero)throw new SafetyException("ExecutableSignatureInvalid");
            var context=Marshal.PtrToStructure<CertificateContext>(certificate);
            if(context.Length==0||context.Length>1024*1024)throw new SafetyException("ExecutableSignatureInvalid");
            var bytes=new byte[checked((int)context.Length)];Marshal.Copy(context.Encoded,bytes,0,bytes.Length);
            return X509CertificateLoader.LoadCertificate(bytes);
        } finally {
            if(certificate!=IntPtr.Zero)CertFreeCertificateContext(certificate);
            if(info!=IntPtr.Zero)Marshal.FreeHGlobal(info);
            if(message!=IntPtr.Zero)CryptMsgClose(message);
            if(store!=IntPtr.Zero)CertCloseStore(store,0);
        }
    }

}
