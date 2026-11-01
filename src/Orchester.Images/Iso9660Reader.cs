using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Orchester.Core;
namespace Orchester.Images;

public sealed record IsoFile(string Path, long Offset, long Length);
public sealed class Iso9660Reader : IAsyncDisposable {
    private sealed record Entry(uint Extent, uint Length, bool Directory);
    private readonly FileStream stream;
    private Entry? root;
    private Iso9660Reader(FileStream stream) => this.stream = stream;

    public static async Task<Iso9660Reader> OpenAsync(string path, CancellationToken ct) {
        var reader = new Iso9660Reader(new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess));
        try {
            var sector = new byte[2048];
            for (int i = 16; i < 80; i++) {
                await reader.ReadAtAsync(i * 2048L, sector, ct);
                if (Encoding.ASCII.GetString(sector, 1, 5) != "CD001" || sector[6] != 1)
                    throw new SafetyException("UnsupportedImage");
                if (sector[0] == 1) {
                    if (BinaryPrimitives.ReadUInt16LittleEndian(sector.AsSpan(128, 2)) != 2048)
                        throw new SafetyException("UnsupportedImage");
                    reader.root = Parse(sector, 156);
                    return reader;
                }
                if (sector[0] == 255) break;
            }
            throw new SafetyException("UnsupportedImage");
        } catch { await reader.DisposeAsync(); throw; }
    }
    public async Task<IsoFile> FindAsync(string path, CancellationToken ct) {
        if (!path.StartsWith('/') || path.Length > 512 || path.Contains('\\') ||
            path.Split('/').Any(p => p is "." or "..")) throw new SafetyException("UnsafePath");
        var current = root ?? throw new SafetyException("UnsupportedImage");
        foreach (var component in path.Trim('/').Split('/')) {
            if (!current.Directory || current.Length > 16 * 1024 * 1024) throw new SafetyException("UnsupportedImage");
            var bytes = new byte[checked((int)current.Length)];
            await ReadAtAsync(current.Extent * 2048L, bytes, ct);
            Entry? next = null;
            for (int pos = 0; pos < bytes.Length;) {
                int length = bytes[pos];
                if (length == 0) { pos = (pos / 2048 + 1) * 2048; continue; }
                if (length < 34 || length > 2048 - pos % 2048 || pos + length > bytes.Length || bytes[pos + 32] > length - 33)
                    throw new SafetyException("UnsupportedImage");
                string name = Encoding.ASCII.GetString(bytes, pos + 33, bytes[pos + 32]).Split(';')[0];
                if (name.Equals(component, StringComparison.OrdinalIgnoreCase)) { next = Parse(bytes, pos); break; }
                pos += length;
            }
            current = next ?? throw new SafetyException("ImageFileMissing");
        }
        if (current.Directory || current.Length == 0) throw new SafetyException("ImageFileMissing");
        long offset = current.Extent * 2048L;
        if (offset > stream.Length - current.Length) throw new SafetyException("UnsupportedImage");
        return new(path, offset, current.Length);
    }
    public async Task<StagedFile> ExtractAsync(IsoFile file, string destination, string relativePath, CancellationToken ct) {
        if (file.Offset < 0 || file.Length <= 0 || file.Offset > stream.Length - file.Length)
            throw new SafetyException("UnsupportedImage");
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        stream.Position = file.Offset;
        var buffer = new byte[1024 * 1024]; long remaining = file.Length;
        while (remaining > 0) {
            int count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
            if (count == 0) throw new EndOfStreamException();
            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), ct); remaining -= count;
        }
        await output.FlushAsync(ct); output.Flush(true);
        return new(relativePath, file.Length, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }
    private static Entry Parse(byte[] bytes, int pos) {
        if (pos + 34 > bytes.Length || bytes[pos] < 34 || bytes[pos + 26] != 0 || bytes[pos + 27] != 0 || (bytes[pos + 25] & 0x80) != 0)
            throw new SafetyException("UnsupportedImage"); // Interleaved and multi-extent files require another reader.
        uint extent = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 2, 4));
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 10, 4));
        if (extent != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos + 6, 4)) ||
            length != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos + 14, 4))) throw new SafetyException("UnsupportedImage");
        return new(extent, length, (bytes[pos + 25] & 2) != 0);
    }
    private async Task ReadAtAsync(long offset, byte[] bytes, CancellationToken ct) {
        if (offset < 0 || offset > stream.Length - bytes.Length) throw new SafetyException("UnsupportedImage");
        stream.Position = offset; await stream.ReadExactlyAsync(bytes, ct);
    }
    public ValueTask DisposeAsync() => stream.DisposeAsync();
}
