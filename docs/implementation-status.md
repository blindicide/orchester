# Implementation status — 0.3.0-preview.1

This branch implements native guided preparation and recovery. It has a Linux cross-build pipeline; Windows execution and release qualification belong to the Windows operator. A successful compile is not installation evidence.

| Area | Available | Remaining work / limits |
| --- | --- | --- |
| UI | WPF/MVVM, seven stages, five sections, live English/Russian, themes, partition maps, consent and progress | Windows accessibility/performance and device behavior confirmation |
| Images | Resumable HTTPS, authenticated-origin checksum retrieval, SHA-256, bounded ISO9660 inspection/extraction, local imports | No general UDF image reader or integrated OpenPGP key-management UI |
| Storage | Basic UEFI/GPT inspection, shrink limits, Windows-supported NTFS resize, owned EFI/data partition creation/format/mount, immutable plan checks | Encrypted/unknown sources, unrecognized storage drivers, dynamic/Storage Spaces/unsafe disks refused |
| Staging/boot | Real temporary internal partitions, complete ISO copy, extracted distribution shim/GRUB, UUID-based boot config, exact owned Boot#### and one-time BootNext | Each release/firmware combination needs Windows operator qualification; no permanent BootOrder edits |
| Secure Boot | Current firmware db/dbx/PE/SBAT checks and release-qualification requirement | Bundled profiles are unvalidated, so Secure Boot native execution refuses them; qualification is a compiled release-policy change |
| Adapters | Guided Casper for Ubuntu/Mint; Debian hd-media with separately authenticated matching assets | No claim every derivative works; bundled releases are candidates |
| Automation | Strict Ubuntu autoinstall / Debian preseed generators and private credential-storage services | Live unattended execution is unavailable; Mint guided only |
| Orchestration | Connected backend/helper, protected per-owner records, disk/operation locks, write-ahead intents and receipts, progress, explicit reboot request | No arbitrary transaction rollback or blind replay after interruption |
| Recovery | Owned cleanup with dependency preservation, BCD/Windows firmware backup and guarded restore, cancelled-allocation growth, redacted exports | Ambiguous/missing ownership or changed resources require operator investigation |
| Completion | Read-only clean ext4 OS identification, reserved-range binding, version/vendor EFI checks, Windows return and explicit Linux boot confirmation | LVM, encryption, btrfs and unsupported ext4 layouts remain pending; no authenticated installer completion transport |
| Packaging | Self-contained portable artifact, bilingual Inno Setup source, signing script | Setup compilation on Windows, publisher keys/certificate and production signing |
| Updates | Signed metadata/hash/Authenticode verification services | Hosted release channel and update installation UI are not supplied |

## Release status

The native path is no longer compiled off: an elevated helper accepts only enumerated commands and independently authorizes the exact plan. Native write capability is scoped to an authorized operation. This is not a user override of identity, geometry, encryption or boot-chain checks.

The application is not declared production-qualified. Specification acceptance still requires operator evidence on Windows 10/11 and physical hardware, qualified release profiles and signed distribution artifacts. Development does not substitute a large automated validation campaign for the Windows operator's execution.
