using System.Buffers.Binary;
using System.Security.Cryptography;
using Orchester.Core;
namespace Orchester.Security;

public static class PeImage {
    public static byte[] AuthenticodeSha256(string path) {
        byte[] bytes=Load(path);int pe=Headers(bytes),optional=pe+24,sectionCount=U16(bytes,pe+6),optionalSize=U16(bytes,pe+20);
        int checksum=optional+64,certificate=optional+112+4*8,headers=checked((int)U32(bytes,optional+60));
        if(U16(bytes,optional)!=0x20b||optionalSize<152||sectionCount<1||sectionCount>96||headers>bytes.Length||headers<optional+optionalSize+sectionCount*40)throw new SafetyException("ExecutableSignatureInvalid");
        int certOffset=checked((int)U32(bytes,certificate)),certSize=checked((int)U32(bytes,certificate+4));
        if(certOffset<headers||certSize<8||certOffset>bytes.Length-certSize||certOffset+certSize!=bytes.Length)throw new SafetyException("ExecutableSignatureInvalid");
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(bytes.AsSpan(0,checksum));hash.AppendData(bytes.AsSpan(checksum+4,certificate-checksum-4));hash.AppendData(bytes.AsSpan(certificate+8,headers-certificate-8));
        var sections=new List<(int Offset,int Length)>();
        for(int i=0;i<sectionCount;i++){int entry=optional+optionalSize+i*40;int length=checked((int)U32(bytes,entry+16)),offset=checked((int)U32(bytes,entry+20));if(length>0)sections.Add((offset,length));}
        int end=headers;
        foreach(var section in sections.OrderBy(s=>s.Offset)) {
            // Reject ambiguous layouts instead of using a different digest algorithm from firmware.
            if(section.Offset!=end||section.Length>certOffset-section.Offset)throw new SafetyException("ExecutableSignatureInvalid");
            hash.AppendData(bytes.AsSpan(section.Offset,section.Length));end=section.Offset+section.Length;
        }
        if(end>certOffset)throw new SafetyException("ExecutableSignatureInvalid");
        hash.AppendData(bytes.AsSpan(end,certOffset-end));return hash.GetHashAndReset();
    }
    public static string? SectionText(string path,string name) {
        byte[] bytes=Load(path);int pe=Headers(bytes),count=U16(bytes,pe+6),table=pe+24+U16(bytes,pe+20);
        if(count>96||table>bytes.Length-count*40)throw new SafetyException("ExecutableSignatureInvalid");
        for(int i=0;i<count;i++){int p=table+i*40;if(System.Text.Encoding.ASCII.GetString(bytes,p,8).TrimEnd('\0')!=name)continue;int length=checked((int)U32(bytes,p+16)),offset=checked((int)U32(bytes,p+20));if(length<0||length>1024*1024||offset<0||offset>bytes.Length-length)throw new SafetyException("ExecutableSignatureInvalid");return System.Text.Encoding.UTF8.GetString(bytes,offset,length).TrimEnd('\0');}
        return null;
    }
    private static byte[] Load(string path){long size=new FileInfo(path).Length;if(size<128||size>32*1024*1024)throw new SafetyException("ExecutableSignatureInvalid");return File.ReadAllBytes(path);}
    private static int Headers(byte[] bytes){if(bytes[0]!='M'||bytes[1]!='Z')throw new SafetyException("ExecutableSignatureInvalid");int pe=checked((int)U32(bytes,60));if(pe<64||pe>bytes.Length-264||U32(bytes,pe)!=0x4550||U16(bytes,pe+4)!=0x8664)throw new SafetyException("ExecutableSignatureInvalid");return pe;}
    private static ushort U16(byte[] b,int p)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p,2));
    private static uint U32(byte[] b,int p)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p,4));
}
