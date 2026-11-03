# ORCHESTER

**Optimized Robust Centralized Host Environment Staging, Transfer and Execution Runtime**

Native Windows x64 dual-boot preparation utility, written in C# / .NET 10 with WPF and MVVM. English and Russian can be switched without restarting. No removable media, account, advertisements, telemetry or persistent service is required.

## Current implementation: 0.3.0-preview.1

The application connects image verification, an exact storage plan, elevated preparation, internal installer staging and one-time UEFI boot. These are real storage and firmware operations; the review and consent screens precede execution.

- Seven-stage guided installation wizard, five navigation sections, light/dark themes and current/proposed partition maps.
- Resumable HTTPS downloads, bounded publisher checksum retrieval, streaming SHA-256 verification and ISO inspection.
- Windows storage discovery, supported NTFS shrinking, dedicated installer EFI/data partitions and independent identity/geometry revalidation.
- Ubuntu/Mint Casper ISO staging; Debian hd-media staging with separately verified matching kernel/initrd.
- Temporary owned firmware entry and BootNext, preserving Windows boot files and the permanent boot order.
- Administrator helper with typed named-pipe commands, client identity verification, protected operation store, per-disk serialization and durable critical-action journal.
- Management/recovery UI, owned-resource inspection and cleanup, saved Windows BCD recovery, guarded cancelled-allocation reclamation and redacted exports.
- Installed ext4 system identification and completion confirmation after returning to Windows, with explicit successful Linux boot confirmation.
- Portable self-contained Windows artifact and bilingual Inno Setup installer source.

**Windows execution has not been validated in this environment.** Catalog profiles are candidate configurations, not advertised qualified releases. All are currently unvalidated: Secure Boot execution therefore refuses them. Encrypted/unknown source volumes are refused; ORCHESTER does not disable encryption or Secure Boot. Automated configuration generators exist, but live execution is guided-only. See [implementation status](docs/implementation-status.md).

## Get a build

GitHub Actions → **Build** → latest successful branch run → **Orchester-win-x64**. Extract the entire folder and run `Orchester.App.exe`. The runtime is included. Keep the helper and all other files beside the application.

Automatic CI runs on Linux and cross-compiles/publishes Windows binaries. Windows jobs are explicitly operator-triggered. No Windows tests run automatically.

To build on Windows 10 22H2 x64 / Windows 11 x64 with the .NET 10 SDK:

```powershell
dotnet build Orchester.slnx -c Release
dotnet publish src/Orchester.App -c Release -r win-x64 --self-contained true -o artifacts/Orchester
dotnet publish src/Orchester.Helper -c Release -r win-x64 --self-contained true -o artifacts/Orchester
```

## Guided workflow

1. Select the matching distribution/release and import or download its ISO. Obtain an authentic publisher digest; official HTTPS checksum retrieval is available where configured. An unsigned mirror checksum alone is not independent authentication.
2. Verify the image. For Debian provide the matching, authenticated hd-media kernel and initrd as well.
3. Inspect eligible storage, choose the NTFS source and Linux allocation, then review both layouts and every proposed change.
4. Save the exact reviewed plan, export the personalized installation guide and confirm storage consent and backup availability.
5. Prepare through UAC. ORCHESTER shrinks the selected NTFS volume and creates its own installer partitions. Linux destination space stays unallocated for the native installer.
6. Request one-time installer boot, then restart explicitly. Follow the exported guide; preserve original partitions and Windows EFI files.
7. Boot Linux successfully, return to Windows, inspect the operation and confirm installation. Clean up verified temporary resources through Recovery.

See the concise [Windows handoff](docs/windows-handoff.md) before native execution. The application never promises full rollback of partitioning or installation.

Local UI data is under `%LOCALAPPDATA%\Orchester`; privileged operation records and image cache are under `%ProgramData%\Orchester\Operations`. Full manifests contain device identities and local paths. Use the redacted export for sharing.

## Documentation

- [Implementation status](docs/implementation-status.md)
- [Windows handoff](docs/windows-handoff.md)
- [Architecture](docs/architecture.md)
- [Execution workflow](docs/execution-services.md)
- [Recovery](docs/recovery.md)
- [Distribution profiles](docs/adapters.md)
- [Packaging and signing](packaging/README.md)

Windows replacement, arbitrary Linux removal, BIOS/MBR, ARM and third-party adapter scripts are excluded.
