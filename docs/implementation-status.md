# Specification 1.0-draft implementation status

Specification supplied 2026-11-01. The initial repository was empty. The implementation establishes the v0.1 foundation and explicit gates for later roadmap stages. This document describes current behavior, not a claim of production acceptance.

| Specification sections | Implemented | Remaining acceptance work |
| --- | --- | --- |
| 1–2 preservation/platform/modes | UEFI/client Windows x64 host gates; GPT/geometry/encryption/health checks; guided preview; excluded storage diagnosed without writes | Windows 10/11, virtual and physical host qualification; detailed dynamic/Storage Spaces/provider classification |
| 3 UI/localization | WPF .NET 10, MVVM, all five sections, seven stages, independent live en-US/ru-RU, themes, native keyboard controls, DPI manifest, current/proposed storage maps | Screen-reader and multi-monitor usability testing; large multi-disk visual stress tests |
| 4 image management | Bundled versioned catalog; signed catalog verification API with caller-supplied pinned RSA key; HTTPS resume/progress/throttle; authenticated user digest gate; streamed SHA-256; ISO9660/PE inspection | Publisher key provisioning, authenticated catalog update transport, automated publisher signature verification, UDF/Joliet/Rock Ridge coverage and full image-format qualification |
| 5 storage | Windows-supported read-only storage discovery; supported shrink-limit query; immutable plans, geometry checks, revalidation and protected-partition refusal | Elevated helper, filesystem consistency checks, dynamic disk qualification, shrink/create/format/mount/owned deletion using validated Windows services |
| 6 staging | Additional staging budget and proposed layout; manifests retain original/proposed disk data and empty ownership lists | Validated staging copy/layout implementation, FAT32 file constraints and alternative staging profiles |
| 7 boot | UEFI and Secure Boot inspection; explicit refusal of mutation methods | Firmware/BCD discovery, validated signed boot chain, one-time entry creation, boot diagnostics and repair |
| 8 adapters | Independently namespaced official adapters and release-specific declarative profiles; guided-only Mint; execution/automation refusal | Real release-specific preparation, Subiquity/autoinstall, Debian preseed and validated boot recipes |
| 9 orchestration | Required phase enumeration, legal transition policy, durable flush before phase persistence, recovery on incomplete records, no reboot-as-completion inference | Execution coordinator, external installer evidence transport, validated completion and resource cleanup |
| 10 security | Unelevated UI manifest, typed helper protocol/policy, SHA-256 fingerprint, independent revalidation, fail-closed mutation, no credential collection or recovery-key logging | Secured named-pipe helper with peer authentication/ACL, Authenticode, authenticated updater, secret-material ACL/hash lifecycle, BitLocker suspend/resume only after validation |
| 11 architecture | All named projects, .NET 10/MSBuild solution, JSON, WPF resources and GitHub Actions | Production IPC executable and full system integration |
| 12 settings/diagnostics | Language, theme, directories, bandwidth and preference persistence; host/disk/operation inspection; redacted JSON export | Implement update checking and automatic image retention actions after lifecycle completion; readable live execution logs |
| 13 installations | Saved operation inspection explicitly distinguished from installation evidence; no removal | OS identification, verified root/boot association and installed-system inventory |
| 14 performance | Streaming hashing/downloading, bounded ISO directories, no background service | Benchmark reference hardware; discovery currently has a 30-second cancellation timeout rather than a demonstrated five-second target |
| 15 tests | Portable deterministic safety harness, localization/XML checks, Windows and Linux CI, documented integration/VM matrix | Actual VM installation tests, Secure Boot/BitLocker tests and physical hardware evidence |
| 16–18 roadmap/acceptance | Foundation preview and release checklist | v0.2–v1.0 acceptance gates remain open; no advertised production-validated releases |

A profile's `validated` flag is evidence metadata, not a security switch. This build still rejects every mutation even if the flag is edited. Profile filenames and an x64 PE header are insufficient proof of publisher identity, installer compatibility or Secure Boot trust.

No remote catalog/update scheduler, unattended installer, arbitrary elevated command, boot-order modification, partition write or reboot is simulated as successful. Pending functionality raises localized, explicit errors.
