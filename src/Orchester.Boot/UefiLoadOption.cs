using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Orchester.Core;
namespace Orchester.Boot;

public static class UefiLoadOption {
    public static byte[] Create(Guid operationId, OwnedPartition partition, int sectorSize, string efiPath) {
        if (operationId == Guid.Empty || partition.OperationId != operationId || partition.PartitionId == Guid.Empty ||
            partition.Number < 1 || sectorSize is not (512 or 4096) || partition.Offset <= 0 || partition.Length <= 0 ||
            partition.Offset % sectorSize != 0 || partition.Length % sectorSize != 0 ||
            !System.Text.RegularExpressions.Regex.IsMatch(efiPath, @"\A\\EFI\\ORCHESTER\\[a-zA-Z0-9_-]+\.efi\z"))
            throw new SafetyException("BootTargetUnsafe");
        byte[] description = Encoding.Unicode.GetBytes("ORCHESTER " + operationId.ToString("D") + "\0");
        byte[] fileName = Encoding.Unicode.GetBytes(efiPath + "\0");
        int pathLength = checked(42 + 4 + fileName.Length + 4);
        var bytes = new byte[6 + description.Length + pathLength];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 1); // LOAD_OPTION_ACTIVE
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), checked((ushort)pathLength));
        description.CopyTo(bytes, 6); int pos = 6 + description.Length;
        bytes[pos] = 4; bytes[pos + 1] = 1; bytes[pos + 2] = 42; // GPT hard-drive media node
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(pos + 4, 4), checked((uint)partition.Number));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(pos + 8, 8), (ulong)(partition.Offset / sectorSize));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(pos + 16, 8), (ulong)(partition.Length / sectorSize));
        partition.PartitionId.TryWriteBytes(bytes.AsSpan(pos + 24, 16)); bytes[pos + 40] = 2; bytes[pos + 41] = 2;
        pos += 42; bytes[pos] = 4; bytes[pos + 1] = 4;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(pos + 2, 2), checked((ushort)(4 + fileName.Length)));
        fileName.CopyTo(bytes, pos + 4); pos += 4 + fileName.Length;
        bytes[pos] = 0x7f; bytes[pos + 1] = 0xff; bytes[pos + 2] = 4;
        return bytes;
    }
    public static FirmwareBootEntry Parse(ushort number, byte[] bytes) {
        if (bytes.Length < 12 || bytes.Length > 1024 * 1024) throw new SafetyException("FirmwareMalformed");
        int descriptionEnd = 6;
        while (descriptionEnd + 1 < bytes.Length && (bytes[descriptionEnd] != 0 || bytes[descriptionEnd + 1] != 0)) descriptionEnd += 2;
        if (descriptionEnd + 2 > bytes.Length) throw new SafetyException("FirmwareMalformed");
        string description = Encoding.Unicode.GetString(bytes, 6, descriptionEnd - 6);
        int pos = descriptionEnd + 2, end = checked(pos + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2)));
        if (end > bytes.Length) throw new SafetyException("FirmwareMalformed");
        Guid? partition = null; string path = ""; bool terminated = false;
        while (pos < end) {
            if (pos + 4 > end) throw new SafetyException("FirmwareMalformed");
            int length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(pos + 2, 2));
            if (length < 4 || pos + length > end) throw new SafetyException("FirmwareMalformed");
            if (bytes[pos] == 4 && bytes[pos + 1] == 1) {
                if (length != 42) throw new SafetyException("FirmwareMalformed");
                if (bytes[pos + 40] == 2 && bytes[pos + 41] == 2) partition = new Guid(bytes.AsSpan(pos + 24, 16));
            }
            if (bytes[pos] == 4 && bytes[pos + 1] == 4) {
                if (length < 6 || length % 2 != 0 || bytes[pos + length - 1] != 0 || bytes[pos + length - 2] != 0)
                    throw new SafetyException("FirmwareMalformed");
                path += Encoding.Unicode.GetString(bytes, pos + 4, length - 6);
            }
            if (bytes[pos] == 0x7f && bytes[pos + 1] == 0xff) {
                if (length != 4 || pos + length != end) throw new SafetyException("FirmwareMalformed");
                terminated = true;
            }
            pos += length;
        }
        if (!terminated) throw new SafetyException("FirmwareMalformed");
        return new(number, description, path, partition,
            (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)) & 1) != 0,
            path.Equals(@"\EFI\Microsoft\Boot\bootmgfw.efi", StringComparison.OrdinalIgnoreCase),
            Convert.ToHexString(SHA256.HashData(bytes)));
    }
}
