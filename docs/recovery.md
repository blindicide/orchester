# Recovery

Open **Recovery**, load operations and inspect the selected operation before cleanup. Native records live under `%ProgramData%\Orchester\Operations`; UI-only plans and downloaded settings live under `%LOCALAPPDATA%\Orchester`. Preserve these records after interruption.

## Before storage writes

Downloads can be cancelled/resumed with the same expected digest. A saved plan does not change storage. Failed preflight may leave a protected source cache; cleanup removes it without touching partitions.

## Interrupted preparation

An unmatched flushed critical intent is `RECOVERY_REQUIRED`, even if partition creation did not finish. Reinspect the actual disk. Never replay an operation automatically. NTFS shrink is not reversed by deleting a record. Ownership must be established from exact GUID/geometry receipts, not labels, filesystem type or proximity.

Cleanup removes a verified owned temporary firmware entry and only verified owned partitions/files. Changed boot options, BootOrder references, foreign files, missing receipts or inaccessible filesystems may require manual investigation. A partition containing an installed loader is retained in full; cleanup does not remove its shared EFI files. An interrupted unformatted partition may require manual recovery if ownership/filesystem state cannot be established safely.

After a cancelled preparation with all owned temporary partitions removed, **Reclaim cancelled allocation** can expand the original source only when the complete original range remains unallocated, original identities/layout still match, Windows reports it safe, and separate recovery consent is supplied. It does not reclaim Linux storage or infer that Linux is absent from a filesystem name.

## Installed systems

Boot Linux successfully, then return to Windows. Confirm the successful Linux boot and request verification. Completion requires an identified matching clean ext4 root in the reserved range, vendor boot evidence and a Windows reboot after preparation. Unsupported LVM/encryption/btrfs/dirty layouts remain pending. An installer reboot alone is not completion.

Run cleanup only after the installer has stopped using its source. Installed boot dependencies remain. Automatic Linux removal is unavailable.

## Windows boot recovery

**Restore saved Windows boot configuration** is separately authorized. It verifies the saved BCD checksum and Windows build, exports current configuration before import, and restores a missing original Windows firmware entry only when its slot is free. Changed or unrelated firmware entries are not overwritten. BCD import can restore older settings: inspect the saved operation before approving it. If Windows cannot boot, use the normal Windows recovery environment and saved diagnostics; ORCHESTER does not run outside Windows.

## Encryption and exports

ORCHESTER never disables Secure Boot, routinely decrypts a disk or stores recovery keys. Encrypted/locked/unknown source volumes are refused. Preserve recovery-key access independently.

Native Windows command failures are retained in the protected local `native-errors.jsonl` with component, action, exit code and bounded Windows error output. This local file may contain paths; the shared export excludes it.

Use **Export operation diagnostics** for sharing. Full local manifests contain identities and paths. The uninstaller refuses active preparation and preserves operation records, installed systems and downloaded images.
