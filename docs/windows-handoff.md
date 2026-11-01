# Windows operator handoff

## Launch

Download the latest successful **Build → Orchester-win-x64** artifact, extract it and launch `Orchester.App.exe`. Keep its companion files together. The portable build is self-contained; setup compilation and production signing are separate packaging steps.

Supported host: Windows 10 22H2 x64 or Windows 11 x64, UEFI/GPT/basic disk, healthy unencrypted NTFS source. The UI runs without elevation; explicit native actions request UAC. Normal automatic CI does not run Windows jobs.

## First native run

Use the distribution ISO matching its catalog candidate. Verify publisher SHA-256. Debian also needs matching authenticated hd-media assets. Current profiles are unvalidated and consequently do not execute with Secure Boot enabled; the application never changes Secure Boot. Do not interpret this candidate limitation as an instruction to change firmware security settings.

Select source/allocation, compare both layouts, export the installation guide, approve the exact plan and explicitly authorize preparation. Preparation performs real supported NTFS shrinking and owned installer partition creation. The source cache and staged ISO require additional free space.

Request the one-time boot only after preparation succeeds. Restart explicitly. Use the native installer's manual/custom storage view with the exported reserved range, preserve all original partitions and use the designated owned EFI partition without reformatting it. Return through a successful Linux boot and then Windows. Verify completion and inspect cleanup.

## What needs your Windows session

- Launch and inspect WPF, UAC and disk discovery on your Windows system.
- Execute the native preparation and one-time installer boot, complete guided installation, boot both operating systems and inspect recovery/cleanup.
- Confirm release/firmware Secure Boot behavior before qualifying a profile.
- Compile setup if wanted; supply production certificate/update trust material for signed distribution.

No Windows execution is claimed by this branch's Linux build. Preserve the operation UUID and export redacted diagnostics if an action fails. The recovery page describes interrupted preparations and the separately consented repair actions.
