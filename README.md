# ORCHESTER

**Optimized Robust Centralized Host Environment Staging, Transfer and Execution Runtime**

Native Windows x64 utility for preparing Debian/Ubuntu dual boot without removable media. Built with C#, .NET 10, WPF and MVVM. English and Russian are available at runtime.

## Current build: execution-services engineering preview

The native bilingual application supports disk inspection, image verification, exact dry-run planning, **real directory staging bundles**, recovery diagnostics and **elevated read-only firmware inspection**. It also contains typed, tested services for Windows storage operations, UEFI one-time boot ownership, persistent execution, Ubuntu/Debian configuration previews, secure temporary credentials, completion evidence and authenticated updates.

**It does not yet perform OS installation.** Native partition/firmware writes are compiled off. Production backend composition, trusted boot-chain deployment, privileged mutation authorization, installer completion transport and release-specific VM/hardware qualification remain open. All bundled release profiles are unvalidated. Read the [requirements matrix](docs/implementation-status.md) for exact status.

Implemented additions since the foundation:

- Bounded ISO9660 extraction and source-authenticated staging receipts; explicit FAT32 limits, exclusive workspaces and interruption detection.
- GPT UEFI device-path parsing, boot inventory, temporary-entry/BootNext policies and verified cleanup, with gated native firmware APIs.
- A separate administrator helper with SID-restricted named pipes, launched-process verification, nonce authentication, bounded frames and read-only typed commands.
- Fixed Windows-supported shrink/create/format/delete code, topology/ownership revalidation, and a flushed write-ahead execution coordinator. Native writes remain gated.
- Ubuntu autoinstall and Debian preseed previews; strong hashed-password input, private temporary storage, no automatic Debian partition selection and guided-only Mint.
- Completion evidence/installation registry and dependency-aware cleanup services.
- Publisher-signed update metadata, downgrade rejection, streamed SHA-256, Authenticode publisher pinning and a production signing script. No update is executed automatically.

The original seven-stage WPF wizard, complete live en-US/ru-RU localization, light/dark themes, resumable downloads, storage visualizer, durable foundation journals and redacted diagnostic exports remain available.

## Build and run

On Windows 10 22H2 x64 or Windows 11 x64, install the .NET 10 SDK:

```powershell
dotnet build Orchester.slnx -c Release
dotnet run --project src/Orchester.App
```

The UI starts as a normal user. Storage inspection can be incomplete without administrative access or the BitLocker module. This is displayed as unknown, never inferred as safe. There is no automatic elevation. A complete dry-run plan requires a host where the read-only queries can obtain every required safety observation.

```powershell
dotnet run --project tests/Unit/Orchester.Tests.csproj -c Release
python tests/check_resources.py
```

The portable core checks also run on Linux with .NET 10. WPF requires Windows. The Windows CI runner uses Windows Server for building, rendering and read-only diagnostics; it does not qualify Windows 10/11 installation support. GitHub Actions builds on Windows, runs core safety checks on both platforms and publishes a self-contained x64 preview artifact.

## Dry-run workflow

1. Select a distribution and inspection release profile.
2. Import a matching ISO or download it. Obtain its SHA-256 from authenticated publisher metadata (for example, a checksum file verified with the publisher's independently obtained signing key). Paste the digest and confirm its provenance. ORCHESTER does not promote a colocated unsigned checksum to publisher trust.
3. Verify the image and inspect its boot components.
4. Choose a healthy eligible GPT disk, unlocked unencrypted NTFS source partition, and Linux allocation in GiB. Staging space is additional to this allocation.
5. Review the current/proposed maps and the explicit change list. Authorize saving the exact dry-run record.
6. Save the operation. The journal remains at `PLAN_APPROVED`, indicating preparation has not executed. The final stage explains the installer handoff gate.
7. Prepare and verify an inspection bundle from stage 6. This copies files only; it does not create bootable installation media.
8. Use Recovery to inspect records, read firmware entries or inspect disk eligibility through UAC, and export redacted diagnostics.

Data is stored beneath `%LOCALAPPDATA%\Orchester`. Manifests contain local device and image paths and should remain local. Shared diagnostic exports exclude those paths, mount letters, raw stable identities and image credentials. No passwords are collected. Logs use stable action codes rendered through complete language packs.

## Documentation

- [Execution services and test preparation](docs/execution-services.md)
- [Architecture and trust boundaries](docs/architecture.md)
- [Requirements and implementation status](docs/implementation-status.md)
- [Recovery procedures](docs/recovery.md)
- [VM and hardware validation matrix](tests/VirtualMachines/README.md)
- [Distribution profile validation](docs/adapters.md)
- [Packaging and signing](packaging/README.md)

No telemetry, ads, mandatory accounts or persistent background service. No Linux removal, Windows replacement, BIOS/MBR modification or arbitrary downloaded scripts.
