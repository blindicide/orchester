using System.Text;

static class IsoFixture {
 public static byte[] Create() {
  var image=new byte[32*2048];
  void Both32(int offset,uint value){System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(offset,4),value);System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(offset+4,4),value);}
  int Entry(int offset,string name,uint extent,uint length,bool directory){var id=Encoding.ASCII.GetBytes(name);int len=33+id.Length;if(len%2!=0)len++;image[offset]=(byte)len;Both32(offset+2,extent);Both32(offset+10,length);image[offset+25]=directory?(byte)2:(byte)0;image[offset+32]=(byte)id.Length;id.CopyTo(image,offset+33);return len;}
  int pvd=16*2048;image[pvd]=1;Encoding.ASCII.GetBytes("CD001").CopyTo(image,pvd+1);image[pvd+6]=1;image[pvd+129]=8;image[pvd+130]=8;Entry(pvd+156,"\0",20,2048,true);
  int root=20*2048;root+=Entry(root,"EFI",21,2048,true);Entry(root,"CASPER",23,2048,true);
  Entry(21*2048,"BOOT",22,2048,true);Entry(22*2048,"BOOTX64.EFI;1",25,512,false);
  int casper=23*2048;casper+=Entry(casper,"VMLINUZ;1",26,32,false);Entry(casper,"INITRD;1",27,32,false);
  int efi=25*2048;image[efi]=(byte)'M';image[efi+1]=(byte)'Z';image[efi+60]=128;image[efi+128]=(byte)'P';image[efi+129]=(byte)'E';image[efi+132]=0x64;image[efi+133]=0x86;
  return image;
 }
}
