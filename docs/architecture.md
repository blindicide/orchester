# Architecture and trust boundaries

The unelevated `Orchester.App` owns the WPF/MVVM shell. `Orchester.Core` defines immutable snapshots, exact plans and typed operation records. `Orchester.Localization` supplies live English/Russian resource dictionaries.

`Orchester.Images` streams hashes and downloads, validates bounded HTTPS redirects and extracts bounded ISO9660 paths. The UI can retrieve checksum metadata from configured official HTTPS origins; mirror-only metadata still requires independent authentication. Catalog data cannot supply commands. Privileged policy reads the catalog embedded in the application assembly, not a user-edited catalog file.

`Orchester.Storage` uses fixed Windows Storage/BitLocker/CIM scripts with JSON stdin and trusted system PowerShell/module paths. Mutation commands are defined in source. NTFS resizing uses Windows filesystem-aware services. Stable disk IDs, GUIDs, original partition geometry, filesystem health, shrink bounds and encryption are independently checked before execution. Unknown observations fail closed. A read-only ext4 parser identifies installed roots; it does not resize or repair them.

`Orchester.Helper` is elevated only on explicit actions. A one-request named pipe has a client SID ACL, nonce, bounded frames and kernel PID checks. Cross-account UAC uses the original GUI account's SID for ownership. The helper verifies the client executable location and, for signed builds, matching publisher identity. It exposes enumerated operations, not arbitrary commands. Startup hooks are disabled. Release signing remains required for production distribution.

`PrivilegedOperationStore` protects `%ProgramData%\Orchester\Operations` for Administrators/SYSTEM and checks owners/ACLs. It persists the exact approved fingerprint, initiating owner SID, owned partitions/mounts/boot entries and recovery checkpoints. Per-operation and per-disk locks serialize mutations. Image inputs are opened under client impersonation, copied into protected storage and rehashed. Native mutation capability is granted only inside the authorized helper scope.

`WindowsInstallationBackend` composes Windows NTFS resize, owned staging partition lifecycle, verified image copy, extracted distribution EFI chain and one-time firmware registration. `ExecutionCoordinator` flushes critical intents before changes and records receipts afterward. An incomplete critical intent requires recovery; existing operations are not blindly replayed.

`Orchester.Boot` reads/writes UEFI variables through typed interfaces. The existing Windows ESP is not used for staging. BootOrder is preserved. Foreign BootNext, changed owned load options and unexpected dependencies block conflicting operations. Secure Boot uses firmware trust/revocation/SBAT inspection and a separately qualified release profile; it is never disabled.

`NativeOperationLifecycle` verifies installed ext4 identity and reserved geometry, expected vendor EFI evidence, return to Windows and user-confirmed Linux boot. A reboot request alone is never completion. Cleanup preserves partitions containing foreign files or firmware dependencies. Saved BCD restoration and source-allocation reclamation require their own explicit recovery consent.

Shared diagnostics are allowlist projections. Full protected records stay local. The application collects no installation passwords during guided setup and never logs encryption recovery keys. No persistent service or downloaded adapter code is installed.
