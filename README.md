# ORCHESTER

**Optimized Robust Centralized Host Environment Staging, Transfer and Execution Runtime**

Native Windows x64 utility for preparing Debian/Ubuntu dual boot without removable media. Built with C#, .NET 10, WPF and MVVM. English and Russian are available at runtime.

## Current release: 0.1 foundation preview

This repository implements the read-only foundation and the contracts for subsequent execution releases. **It does not yet install an operating system.** No storage mutation, firmware mutation, automatic reboot, unattended configuration, or boot repair is enabled. Those boundaries fail closed in the engine as well as the UI. A production 1.0 release requires the installation and hardware evidence described in [implementation status](docs/implementation-status.md).

Implemented:

- Seven-stage bilingual wizard, five navigation sections, persistent settings, light/dark appearance, native accessible controls, per-monitor DPI support.
- Windows Storage cmdlet/CIM discovery of disk identities, GPT GUIDs, geometry, filesystem health, encryption observations and supported NTFS shrink limits. Unknown or unavailable observations remain unknown and block planning.
- Versioned non-executable distribution catalog, built-in Ubuntu, Debian and Linux Mint inspection adapters. Every bundled release is explicitly **unvalidated** for execution.
- Resumable streamed HTTPS downloads, progress/rate reporting, bandwidth limiting, SHA-256 verification with independently authenticated user-supplied digests, and local ISO import.
- Bounded ISO9660 inspection of profile kernel/initrd paths and PE x64 EFI components. This does not validate Secure Boot signatures or identify an OS from filenames alone.
- Immutable partition plans, storage revalidation, explicit dry-run approval, current/proposed partition maps, staging capacity calculation.
- Durable JSON manifests and JSON Lines journal, guarded state transitions, safe cancellation, interrupted-record detection and diagnostic exports with an explicit field allowlist.
- Typed helper request contract and independent plan policy. Privileged mutation commands are rejected; no elevated helper is shipped in this preview.

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

The portable core checks also run on Linux with .NET 10. WPF requires Windows. GitHub Actions builds on Windows, runs core safety checks on both platforms and publishes a self-contained x64 preview artifact.

## Dry-run workflow

1. Select a distribution and inspection release profile.
2. Import a matching ISO or download it. Obtain its SHA-256 from authenticated publisher metadata (for example, a checksum file verified with the publisher's independently obtained signing key). Paste the digest and confirm its provenance. ORCHESTER does not promote a colocated unsigned checksum to publisher trust.
3. Verify the image and inspect its boot components.
4. Choose a healthy eligible GPT disk, unlocked unencrypted NTFS source partition, and Linux allocation in GiB. Staging space is additional to this allocation.
5. Review the current/proposed maps and the explicit change list. Authorize saving the exact dry-run record.
6. Save the operation. The journal remains at `PLAN_APPROVED`, indicating preparation has not executed. The final stage explains the installer handoff gate.
7. Use Recovery to inspect records and export redacted diagnostics.

Data is stored beneath `%LOCALAPPDATA%\Orchester`. Manifests contain local device and image paths and should remain local. Shared diagnostic exports exclude those paths, mount letters, raw stable identities and image credentials. No passwords are collected. Logs use stable action codes rendered through complete language packs.

## Documentation

- [Architecture and trust boundaries](docs/architecture.md)
- [Requirements and implementation status](docs/implementation-status.md)
- [Recovery procedures](docs/recovery.md)
- [VM and hardware validation matrix](tests/VirtualMachines/README.md)
- [Distribution profile validation](docs/adapters.md)
- [Packaging and signing](packaging/README.md)

No telemetry, ads, mandatory accounts or persistent background service. No Linux removal, Windows replacement, BIOS/MBR modification or arbitrary downloaded scripts.
