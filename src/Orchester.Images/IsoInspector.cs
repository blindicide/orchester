using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using Orchester.Core;
namespace Orchester.Images;
// Bounded ISO9660 inspection; unsupported UDF-only, compressed and malformed images fail closed.
public static class IsoInspector {
 private sealed record Entry(uint Extent,uint Length,bool Directory);
 public static async Task<ImageInspection> InspectAsync(string path,ReleaseProfile profile,CancellationToken ct) {
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,4096,FileOptions.Asynchronous|FileOptions.RandomAccess);
  var findings=ImmutableArray.CreateBuilder<string>();
  try {
   var sector=new byte[2048]; Entry? root=null;
   for(int i=16;i<80;i++) {
    await ReadAtAsync(stream,i*2048L,sector,ct);
    if(Encoding.ASCII.GetString(sector,1,5)!="CD001" || sector[6]!=1) throw new SafetyException("UnsupportedImage");
    if(sector[0]==1) { if(BinaryPrimitives.ReadUInt16LittleEndian(sector.AsSpan(128,2))!=2048) throw new SafetyException("UnsupportedImage"); root=ParseEntry(sector,156); break; }
    if(sector[0]==255) break;
   }
   if(root is null) throw new SafetyException("UnsupportedImage");
   async Task<Entry?> Find(string isoPath) {
    Entry current=root;
    foreach(var name in isoPath.Trim('/').Split('/')) {
     if(!current.Directory || current.Length>16*1024*1024) return null;
     var data=new byte[checked((int)current.Length)]; await ReadAtAsync(stream,checked(current.Extent*2048L),data,ct); Entry? next=null;
     for(int pos=0;pos<data.Length;) {
      int length=data[pos]; if(length==0) {pos=((pos/2048)+1)*2048;continue;}
      if(length<34 || pos+length>data.Length || data[pos+32]>length-33) throw new SafetyException("UnsupportedImage");
      string entryName=Encoding.ASCII.GetString(data,pos+33,data[pos+32]).Split(';')[0];
      if(entryName.Equals(name,StringComparison.OrdinalIgnoreCase)) {next=ParseEntry(data,pos);break;}
      pos+=length;
     }
     if(next is null) return null; current=next;
    }
    return current;
   }
   var efi=await Find("/EFI/BOOT/BOOTX64.EFI"); bool x64=false;
   if(efi is { Directory:false,Length:>=64 }) {
    var header=new byte[64]; long offset=efi.Extent*2048L; await ReadAtAsync(stream,offset,header,ct);
    int pe=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(60,4));
    if(header[0]=='M' && header[1]=='Z' && pe>=64 && pe<=efi.Length-6) {
     var signature=new byte[6]; await ReadAtAsync(stream,offset+pe,signature,ct);
     x64=signature[0]=='P' && signature[1]=='E' && signature[2]==0 && signature[3]==0 && BinaryPrimitives.ReadUInt16LittleEndian(signature.AsSpan(4,2))==0x8664;
    }
   }
   var kernel=await Find(profile.KernelPath); var initrd=await Find(profile.InitrdPath);
   if(!x64) findings.Add("MissingX64Efi"); if(kernel is not {Directory:false,Length:>0}) findings.Add("MissingKernel"); if(initrd is not {Directory:false,Length:>0}) findings.Add("MissingInitrd");
   return new(true,x64,kernel is {Directory:false,Length:>0},initrd is {Directory:false,Length:>0},findings.ToImmutable());
  } catch(Exception e) when(e is SafetyException or EndOfStreamException or OverflowException) { return new(false,false,false,false,["UnsupportedImage"]); }
 }
 private static Entry ParseEntry(byte[] data,int pos) {
  if(pos+34>data.Length || data[pos]<34) throw new SafetyException("UnsupportedImage");
  uint extent=BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos+2,4)), length=BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos+10,4));
  if(extent!=BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos+6,4)) || length!=BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos+14,4))) throw new SafetyException("UnsupportedImage");
  return new(extent,length,(data[pos+25]&2)!=0);
 }
 private static async Task ReadAtAsync(FileStream stream,long offset,byte[] data,CancellationToken ct) {
  if(offset<0 || offset>stream.Length-data.Length) throw new EndOfStreamException();
  stream.Position=offset; await stream.ReadExactlyAsync(data,ct);
 }
}
