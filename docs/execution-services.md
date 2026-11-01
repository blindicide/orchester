# Execution services and hardware-test preparation

## What can be exercised now

Build the solution and use a normal-user UI session to download/import/verify an ISO and save a dry-run plan. On stage 6, **Prepare directory bundle** copies the authenticated image and extracts the profile's kernel, initrd and inspected x64 EFI component into `<staging-directory>/<operation-id>/`. **Verify directory bundle** independently rehashes the source image, resolves the expected ISO files and compares every staged artifact. Unknown extra files, symlinks, changed receipts/files and incomplete preparations are rejected. Preparing an already existing operation directory refuses to overwrite it.

The bundle is for inspection; it does not include a certified boot chain and is not advertised as bootable. An extracted `BOOTX64.EFI` can be a shim requiring additional binaries and firmware trust. EFI signature and chain validation must precede actual boot deployment. A FAT32 budget check refuses an image at the single-file limit. Debian DVD layouts require separate authenticated matching hd-media kernel/initrd assets; they cannot use the DVD initrd as a replacement.

In Recovery, **Inspect firmware (administrator consent)** starts the separate diagnostic helper through Windows UAC. The helper reads UEFI boot variables only. A cancelled elevation, missing helper or missing firmware privilege is shown explicitly. No mutation command is exposed over IPC.

## Programmatic configuration previews

`UnattendedGenerator.Generate(profile, plan, options)` returns a `GeneratedConfiguration` with `PreviewOnly=true`. Ubuntu output is JSON-form YAML following `#cloud-config`, with identity/locale/keyboard/timezone/packages and explicit preserved-disk/partition storage entries. It requires a separately verified Linux disk serial, new root GUID/number, a dedicated Linux EFI GUID/number and formatting consent. Existing partitions from the original Windows layout cannot be selected. The installer environment must revalidate these bindings immediately before execution; number/serial values alone do not replace GUID validation.

Debian output preseeds locale, keyboard, user hash, time zone, mirror and packages; it does not preseed partman confirmations or guess a bootloader disk. Partitioning stays interactive. Mint automation is rejected. Passwords must be precomputed SHA-512 crypt hashes with explicit rounds >=100000, a 16-character salt and a canonical checksum; plaintext passwords are rejected. Never put these configurations in public manifests/logs. Write temporary material with `SecretWorkspace`, whose ACL/mode is set before writing data, and dispose it after consumption.

## Critical operations

`ExecutionCoordinator` persists the approved plan, then flushes each `INTENT` before invoking an `IExecutionBackend` step. Verified receipts advance storage, staging and boot phases. Requesting the next boot is a separate action. A crash leaving an intent without a verified result is `RECOVERY_REQUIRED`; an uncertain boot-variable change is `UNKNOWN_EXTERNAL_STATE`. There is no automatic retry of an existing execution directory and no promise of partition rollback. Cancellation after storage begins requires recovery inspection.

`WindowsStorageOperations` contains fixed supported Storage cmdlets for resizing NTFS and creating, formatting and deleting the precisely recorded staging partition. Disk identities, source geometry, encryption, shrink limits, preserved partition topology and resource ownership are checked independently. System modules are resolved from the Windows system module directory, not a user-controlled module search path. Once a native filesystem resize starts, the process is not killed on a cancellation token. The native gate prevents these code paths from running in this preview.

`UefiBootService` constructs a GPT hard-drive/file device path, creates a previously absent temporary entry, and uses `BootNext`. It never writes `BootOrder`, never overwrites existing entries, and refuses cleanup when ownership bytes change or an external persistent order references the entry. The native backend is gated; the in-memory backend is exercised in CI.

`InstallationRegistry` confirms only when the injected probe independently verifies the expected root/disk/distribution/version, root filesystem, installer receipt, boot entry and both Linux/Windows boot evidence. No native probe or authenticated installer transport is supplied yet. A reboot or an on-disk JSON record is not sufficient completion evidence. Arbitrary Linux removal remains outside scope.

## Before native hardware testing

Use disposable VM/VHD cases first. The current release has no production execution backend or writable helper endpoint. Review the gate checklist in `implementation-status.md`; do not bypass it by editing catalog validation flags. Keep snapshots and independent copies of disk GUIDs/geometry, protected EFI file hashes and firmware variables. The GUI is suitable for read-only hardware inventory and bundle verification now; dual-boot execution qualification follows the remaining integration steps.
