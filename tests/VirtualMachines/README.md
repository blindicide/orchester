# Disposable VM acceptance matrix

No real storage or firmware mutation tests may run on a developer's host. Use disposable snapshot-based VMs and retain before/after evidence. None of the installation cases below has been executed by this initial implementation.

| Environment | Required evidence |
| --- | --- |
| Windows 10 22H2 x64 UEFI/GPT | Dry run, kernel boot, installation, final Windows and Linux boot |
| Windows 11 x64 UEFI/GPT | Same; supported OS build recorded |
| One disk / separate target disks | Stable identity revalidation and correct target offsets |
| Existing ESP/Windows/recovery/Linux partitions | Byte hashes of protected EFI files; original partition GUIDs retained |
| Multiple firmware entries | Default boot order retained; one-time entry only |
| Secure Boot enabled | Signed chain validated; no silent downgrade |
| BitLocker enabled | Recovery access confirmation; validated protection suspension/restoration |
| Unsupported host/dynamic/RAID/Storage Spaces | Inspection only; no automated writes |
| Insufficient shrink space | Refusal before writing; staging included in budget |
| Corrupt metadata / changed identity | Refusal, including after user plan approval |
| Interrupted download / staging / firmware update | Resume or recovery state; no false completion |
| Malformed/unsupported/wrong-architecture ISO | Compatibility refusal before storage changes |
| Each release and Mint edition | Exact checksum, staging, installer, Linux boot, Windows boot, final geometry, verified owned-resource cleanup |
| Ubuntu autoinstall / Debian preseed | No interactive intervention, correct partition target, credential redaction and secure cleanup |

Use a fresh snapshot per case. Save firmware entries, disk identities/geometry, Windows boot file hashes, installer logs, journal, and verified final layout. Include a physical Windows 10 and Windows 11 reference machine only after disposable VM gates pass. Measure startup, memory, discovery and streaming verification on defined reference hardware.
