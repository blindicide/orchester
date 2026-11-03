using System.Buffers.Binary;
using System.Text;
using Orchester.Core;
namespace Orchester.Storage;

// Reads identification files only. It never mounts, repairs, resizes or writes a Linux filesystem.
public sealed class Ext4Identification(FileStream disk,long partitionOffset,long partitionLength,int sectorSize) {
    private uint blockSize,inodesPerGroup,inodeSize,descriptorSize,firstDataBlock;
    private ulong blocks;
    private bool wide;
    private sealed record Inode(ushort Mode,long Length,uint Flags,byte[] Blocks);
    public async Task<(string Id,string Version,Guid FilesystemId)> IdentifyAsync(CancellationToken ct) {
        byte[] sb=await Read(1024,1024,ct);
        if(U16(sb,56)!=0xEF53)throw new SafetyException("InstalledFilesystemUnsupported");
        uint incompat=U32(sb,96), readOnlyFeatures=U32(sb,100);
        if((incompat&0x40)==0||(incompat&(0x4|0x10|0x10000))!=0||(U16(sb,58)&1)==0)throw new SafetyException("InstalledFilesystemUnsupported");
        if((readOnlyFeatures&0x400)!=0&&Crc32C(sb.AsSpan(0,1020))!=U32(sb,1020))throw new SafetyException("InvalidGeometry");
        uint log=U32(sb,24);if(log>6)throw new SafetyException("InvalidGeometry");
        blockSize=1024u<<(int)log;inodesPerGroup=U32(sb,40);inodeSize=U16(sb,88);firstDataBlock=U32(sb,20);wide=(incompat&0x80)!=0;
        descriptorSize=wide?U16(sb,254):32u;blocks=U32(sb,4)|(wide?(ulong)U32(sb,336)<<32:0);
        if(blockSize>65536||inodeSize<128||inodeSize>blockSize||inodesPerGroup==0||descriptorSize<32||descriptorSize>blockSize||blocks>(ulong)partitionLength/blockSize)throw new SafetyException("InvalidGeometry");
        Inode? local=await Find("etc/os-release",ct);
        Inode? osRelease=local is not null&&(local.Mode&0xf000)==0x8000?local:await Find("usr/lib/os-release",ct);
        if(osRelease is null||(osRelease.Mode&0xf000)!=0x8000||osRelease.Length>65536)throw new SafetyException("CompletionEvidenceInsufficient");
        string text=Encoding.UTF8.GetString(await Contents(osRelease,65536,ct));
        var values=new Dictionary<string,string>();
        foreach(string line in text.Split('\n')) {var pair=line.Split('=',2);if(pair.Length==2)values[pair[0].Trim()]=pair[1].Trim().Trim('"','\'');}
        if(!values.TryGetValue("ID",out string? id)||!values.TryGetValue("VERSION_ID",out string? version)||!System.Text.RegularExpressions.Regex.IsMatch(id,@"\A[a-z0-9_-]+\z")||!System.Text.RegularExpressions.Regex.IsMatch(version,@"\A[0-9.]+\z"))throw new SafetyException("CompletionEvidenceInsufficient");
        byte[] uuid=sb.AsSpan(104,16).ToArray();string hex=Convert.ToHexString(uuid);
        return(id,version,Guid.ParseExact(hex,"N"));
    }
    private async Task<Inode?> Find(string path,CancellationToken ct) {
        uint number=2;
        foreach(string component in path.Split('/')) {
            var inode=await LoadInode(number,ct);if((inode.Mode&0xf000)!=0x4000)return null;
            byte[] directory=await Contents(inode,16*1024*1024,ct);uint next=0;
            for(int offset=0;offset<directory.Length;) {
                if(directory.Length-offset<8)throw new SafetyException("InvalidGeometry");
                uint child=U32(directory,offset);int length=U16(directory,offset+4),nameLength=directory[offset+6];
                if(length<8||length%4!=0||length>directory.Length-offset||nameLength>length-8)throw new SafetyException("InvalidGeometry");
                if(child!=0&&Encoding.UTF8.GetString(directory,offset+8,nameLength)==component){next=child;break;}
                offset+=length;
            }
            if(next==0)return null;number=next;
        }
        return await LoadInode(number,ct);
    }
    private async Task<Inode> LoadInode(uint number,CancellationToken ct) {
        if(number==0)throw new SafetyException("InvalidGeometry");
        uint group=(number-1)/inodesPerGroup,index=(number-1)%inodesPerGroup;
        var descriptor=await Read(checked((long)(firstDataBlock+1)*blockSize+(long)group*descriptorSize),(int)descriptorSize,ct);
        ulong table=U32(descriptor,8)|(wide?(ulong)U32(descriptor,40)<<32:0);
        if(table>=blocks)throw new SafetyException("InvalidGeometry");
        byte[] inode=await Read(checked((long)table*blockSize+(long)index*inodeSize),(int)inodeSize,ct);
        long length=checked((long)(U32(inode,4)|((ulong)U32(inode,108)<<32)));
        return new(U16(inode,0),length,U32(inode,32),inode.AsSpan(40,60).ToArray());
    }
    private async Task<byte[]> Contents(Inode inode,int limit,CancellationToken ct) {
        if(inode.Length<0||inode.Length>limit||(inode.Flags&0x80000)==0)throw new SafetyException("InstalledFilesystemUnsupported");
        var result=new byte[(int)inode.Length];int nodes=0;
        async Task Extents(byte[] data,int expectedDepth) {
            if(++nodes>2048||data.Length<12||U16(data,0)!=0xf30a)throw new SafetyException("InvalidGeometry");
            int entries=U16(data,2),depth=U16(data,6);
            if(depth>5||(expectedDepth>=0&&depth!=expectedDepth)||entries>(data.Length-12)/12)throw new SafetyException("InvalidGeometry");
            for(int i=0;i<entries;i++) {
                int pos=12+i*12;ulong logical=U32(data,pos);
                if(logical*blockSize>=(ulong)result.Length)continue;
                if(depth>0) {
                    ulong child=U32(data,pos+4)|((ulong)U16(data,pos+8)<<32);
                    if(child>=blocks)throw new SafetyException("InvalidGeometry");
                    await Extents(await Read(checked((long)child*blockSize),(int)blockSize,ct),depth-1);
                } else {
                    uint length=U16(data,pos+4);if(length>32768)continue;if(length==32768)length=32768;
                    ulong physical=U32(data,pos+8)|((ulong)U16(data,pos+6)<<32);
                    if(length==0||physical>=blocks||length>blocks-physical)throw new SafetyException("InvalidGeometry");
                    long target=checked((long)logical*blockSize);int bytes=(int)Math.Min((long)length*blockSize,result.Length-target);
                    (await Read(checked((long)physical*blockSize),bytes,ct)).CopyTo(result,(int)target);
                }
            }
        }
        await Extents(inode.Blocks,-1);return result;
    }
    private async Task<byte[]> Read(long offset,int length,CancellationToken ct) {
        if(offset<0||length<0||offset>partitionLength-length||length>16*1024*1024)throw new SafetyException("InvalidGeometry");
        long absolute=checked(partitionOffset+offset),aligned=absolute/sectorSize*sectorSize;int prefix=(int)(absolute-aligned);
        int count=checked(((prefix+length+sectorSize-1)/sectorSize)*sectorSize);var buffer=new byte[count];
        disk.Position=aligned;await disk.ReadExactlyAsync(buffer,ct);return buffer.AsSpan(prefix,length).ToArray();
    }
    private static ushort U16(byte[] b,int p)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p,2));
    private static uint U32(byte[] b,int p)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p,4));
    private static uint Crc32C(ReadOnlySpan<byte> bytes) {uint crc=uint.MaxValue;foreach(byte b in bytes){crc^=b;for(int i=0;i<8;i++)crc=(crc>>1)^((crc&1)!=0?0x82f63b78u:0);}return crc;}
}
