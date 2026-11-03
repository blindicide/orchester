# Native execution workflow

1. UI verifies image identity/layout and builds an immutable four-part plan: NTFS shrink, reserved Linux area, 512 MiB installer EFI partition, and installer data partition. The Linux allocation excludes staging overhead.
2. Review approval binds the exact plan fingerprint. Native execution separately requires storage consent, backup confirmation and UAC.
3. Helper verifies client identity, plan, compiled catalog profile, supported host/storage stack, source eligibility and current geometry. It caches and rehashes authorized image inputs and backs up Windows boot configuration before storage writes.
4. Coordinator writes a durable intent, shrinks through Windows Storage services, creates/records/formats/mounts only the two owned temporary partitions, and records the result.
5. ISO is copied under a unique operation path. EFI shim/GRUB and configuration are extracted/generated into the owned ESP. Casper staging uses NTFS data; Debian hd-media uses FAT32 data and rejects oversized images. Files are hashed into protected receipts.
6. A temporary owned firmware entry is registered. A separate action verifies staged files and requests BootNext. Restart is explicit and can be cancelled during the Windows shutdown delay.
7. Native distribution installer performs guided partitioning within the reserved range. Follow the exported guide. ORCHESTER does not execute unattended partitioning.
8. After successful Linux boot and return to Windows, completion checks actual clean ext4 identification, target geometry and firmware evidence. Unsupported evidence remains pending. Recovery removes only verified temporary resources and preserves installed boot dependencies.

## Operation data

Privileged records live under `%ProgramData%\Orchester\Operations\<UUID>`. `operation.json`, approved plan, critical-action log, cached source, boot backups and completion evidence serve distinct purposes. Journal intent is flushed before critical writes. Missing receipts, malformed state and changed hardware are not a reason to guess or replay.

The installer guide lists the physical disk, reserved range and original partition identities. Installer names such as `/dev/sda` can change across operating systems; do not target them by translating a Windows disk number.

## Current bounds

Guided execution only. Catalog candidates are not qualified releases. Secure Boot requires a qualified profile and therefore refuses every currently bundled candidate. Encrypted/unknown sources are refused. No raw NTFS resizing, automatic Windows replacement, Linux removal, foreign bootloader cleanup or background service is implemented.

Ubuntu autoinstall / Debian preseed generators, authenticated update verification and directory-bundle APIs are additional services. Their existence does not activate unattended installation or a hosted update channel.
