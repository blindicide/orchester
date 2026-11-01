using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using Orchester.Core;
namespace Orchester.Boot;

// Firmware policy is testable independently of native firmware. Native writes have a compiled release gate.
public sealed class UefiBootService(IFirmwareVariables variables) {
    public FirmwareSnapshot Inspect() {
        var order = ReadOrder(variables.Read("BootOrder"));
        var entries = ImmutableArray.CreateBuilder<FirmwareBootEntry>();
        foreach (ushort id in order.Distinct()) {
            var bytes = variables.Read(Name(id));
            if (bytes is not null) entries.Add(UefiLoadOption.Parse(id, bytes));
        }
        return new(order, ReadNumber(variables.Read("BootCurrent")), ReadNumber(variables.Read("BootNext")), entries.ToImmutable());
    }
    public BootEntryOwnership Register(Guid operationId, OwnedPartition partition, int sectorSize, string path) {
        byte[] option = UefiLoadOption.Create(operationId, partition, sectorSize, path);
        var order = ReadOrder(variables.Read("BootOrder"));
        // Never modify an existing variable and never change the persistent boot order.
        for (int id = 0x7000; id < 0x7100; id++) {
            ushort number = (ushort)id;
            if (order.Contains(number) || variables.Read(Name(number)) is not null) continue;
            var receipt = new BootEntryOwnership(operationId, number, Convert.ToHexString(SHA256.HashData(option)),
                partition.PartitionId, path, variables.Read("BootNext"));
            variables.Write(Name(number), option);
            ValidateOwned(receipt);
            return receipt;
        }
        throw new SafetyException("BootEntryLimit");
    }
    public void RequestOneTimeBoot(BootEntryOwnership ownership) {
        ValidateOwned(ownership);
        byte[]? current = variables.Read("BootNext");
        // A new third-party one-time boot request must not be overwritten.
        if (!Equal(current, ownership.PreviousBootNext) && ReadNumber(current) != ownership.Number)
            throw new SafetyException("BootConfigurationChanged");
        variables.Write("BootNext", Number(ownership.Number));
        if (ReadNumber(variables.Read("BootNext")) != ownership.Number) throw new SafetyException("BootVerificationFailed");
    }
    public void RemoveOwned(BootEntryOwnership ownership) {
        ValidateOwned(ownership);
        var order = ReadOrder(variables.Read("BootOrder"));
        // If external software added it to a persistent order, dependency review is required.
        if (order.Contains(ownership.Number)) throw new SafetyException("BootConfigurationChanged");
        if (ReadNumber(variables.Read("BootNext")) == ownership.Number) {
            var old = ReadNumber(ownership.PreviousBootNext);
            if (old is not null && variables.Read(Name(old.Value)) is not null) variables.Write("BootNext", ownership.PreviousBootNext!);
            else variables.Delete("BootNext");
        }
        variables.Delete(Name(ownership.Number));
        if (variables.Read(Name(ownership.Number)) is not null) throw new SafetyException("BootVerificationFailed");
    }
    private void ValidateOwned(BootEntryOwnership ownership) {
        var bytes = variables.Read(Name(ownership.Number)) ?? throw new SafetyException("BootOwnershipMismatch");
        if (Convert.ToHexString(SHA256.HashData(bytes)) != ownership.ContentSha256) throw new SafetyException("BootOwnershipMismatch");
        var entry = UefiLoadOption.Parse(ownership.Number, bytes);
        if (entry.ContentSha256 != ownership.ContentSha256 || entry.PartitionId != ownership.PartitionId ||
            entry.EfiPath != ownership.EfiPath || entry.Description != "ORCHESTER " + ownership.OperationId.ToString("D") || entry.WindowsBootManager)
            throw new SafetyException("BootOwnershipMismatch");
    }
    private static bool Equal(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.SequenceEqual(b);
    private static string Name(ushort id) => "Boot" + id.ToString("X4");
    private static byte[] Number(ushort id) { var bytes = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(bytes, id); return bytes; }
    private static ushort? ReadNumber(byte[]? bytes) {
        if (bytes is null) return null;
        if (bytes.Length != 2) throw new SafetyException("FirmwareMalformed");
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes);
    }
    private static ImmutableArray<ushort> ReadOrder(byte[]? bytes) {
        if (bytes is null) return [];
        if (bytes.Length % 2 != 0 || bytes.Length > 131072) throw new SafetyException("FirmwareMalformed");
        var result = ImmutableArray.CreateBuilder<ushort>();
        for (int i = 0; i < bytes.Length; i += 2) result.Add(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i, 2)));
        return result.ToImmutable();
    }
}
