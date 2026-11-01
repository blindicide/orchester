# Specification 1.0-draft implementation status

This is an engineering preview implementing the foundation and substantial v0.2–v1.0 services. It is **not a qualified USB-free installer**. The read-only UI and directory preparation work; native disk/firmware writes are compiled off. All catalog release profiles remain unvalidated.

| Area | Implemented behavior | Still required before production |
| --- | --- | --- |
| Native UI and languages | .NET 10 WPF/MVVM, five sections, seven stages, independent live English/Russian, themes, DPI manifest, current/proposed maps, bundle controls and elevated firmware diagnostics | Narrator, multi-monitor and physical Windows 10/11 usability qualification |
| Storage discovery/plans | Supported Windows Storage queries, stable IDs/GPT GUIDs, health/encryption/shrink observations, read-only NTFS dirty-state query, elevated eligibility inspection, immutable exact-change plans, post-shrink layout policy and protected-partition refusal | Full dynamic/provider classification and filesystem consistency qualification |
| Native storage operations | Fixed Windows-supported shrink/create/format/delete implementations with revalidation and exact ownership checks; no raw NTFS resizing; trusted system PowerShell module path | VM qualification, mount lifecycle, protected helper-owned authorization/receipt store and production backend composition. Native commands remain gated |
| ISO and downloads | HTTPS resume/throttle, authenticated reference digest, streamed SHA-256, bounded ISO9660 lookup/extraction, PE x64 inspection | Publisher image-signature automation, UDF/Joliet/Rock Ridge support and release-specific layout qualification |
| Staging | Real directory preparation through the UI: extracted kernel/initrd/inspected EFI component and verified source ISO; FAT32 file-size refusal; exclusive destination, interruption marker, durable receipt; verification against authenticated source, not receipt claims | Actual staging-partition mounting, complete trusted EFI chain, filesystem/large-file strategies and installer qualification. A directory bundle is not bootable media |
| Firmware | Real elevated read-only UEFI inventory; GPT/file device-path codec; entry registration, one-time BootNext and ownership-checked cleanup service; preservation of BootOrder and foreign BootNext; gated native variable writer | Secure Boot database/component-chain verification, full boot-chain staging, saved-state repair workflow and native-write VM qualification |
| Distribution adapters | Compiled Casper/hd-media recipes and GRUB previews; Ubuntu autoinstall cloud-config generator; Debian locale/user/mirror/package preseed generator; guided-only Mint | Recipe qualification; independently authenticated matching Debian hd-media assets; executable Ubuntu partition binding; Debian automated partition targeting. All generated automation remains preview-only |
| Execution | Persisted approved plan, flushed write-ahead intents, ordered action journal, storage/staging/boot coordinator, separate boot request, no blind restart, cancellation/recovery distinctions, explicit receipts | Wire the qualified native services into a production backend and privileged execution endpoint; installer handoff/return protocol |
| Completion and management | Evidence-based installation registry, exact root/disk/distribution/version binding, installer/root/boot/dual-boot evidence requirements, ownership/dependency cleanup policy | Native installed-root probe, authenticated installer completion transport, Linux and Windows boot evidence collection, management UI integration |
| Privilege separation | Separate requireAdministrator helper; bounded typed named-pipe protocol; SID ACL supports same-user UAC boundary; client validates launched server PID; one-use helper process and nonce; independent host/disk/plan revalidation | Signed production helper identity, protected single-use mutation authorization and durable privileged resource receipts. Current IPC exposes diagnostics only |
| Credentials | Restrictive Windows ACL/Unix mode temporary workspace, no plaintext-password API, strict SHA-512 crypt hash format with at least 100000 rounds/16-character salt; logical cleanup | Installer-side consumption/removal and validation on encrypted hosts. Logical deletion does not claim SSD secure erasure |
| Updates/signing | RSA-PSS authenticated release metadata, downgrade rejection, streamed update hash verification, Windows Authenticode chain and pinned publisher public-key verification, signing script | Provision publisher/update keys and production certificate, signed release service, update-check UI and installation UX. No downloaded update is launched automatically |
| Diagnostics/recovery | Foundation journals, redacted exports, native firmware inventory, execution journal interruption/corruption inspection and guarded resource cleanup | Combined execution recovery UI, boot-configuration repair, protection restoration workflow and operator runbooks validated on real failures |
| Packages | Self-contained app and helper preview bundle in CI; production signing script | Installer package and signed production artifacts |
| Validation | Portable safety suite, language/XML checks, real WPF view/wizard/bundle checks, Windows live read-only inventory and real helper IPC; signature verification checks | All disposable VM installation cases, Secure Boot/BitLocker matrix, performance benchmarks and physical hardware evidence |

## Native-write gate

`ReleaseGate.RequireNativeWrites()` rejects all native mutation calls, regardless of catalog flags. The fake storage/firmware backends used by tests exercise policies without touching host partitions or firmware. This is not a user risk-override switch.

**Removing the gate alone is not an integration procedure.** Before exposing mutation commands, add a protected privileged operation store and single-use approval binding; independently authenticate boot components; compose the production coordinator/backend; qualify each release's exact installer and boot behavior in disposable VMs. The current diagnostic helper deliberately has no mutation command enum or arbitrary command-string endpoint.

## Remaining acceptance sequence

1. Qualify Windows storage methods against attached disposable VHDs, including interruption and changed-ID cases.
2. Build and authenticate a complete Ubuntu/Debian boot chain, with UUID-based source selection and required Debian hd-media components.
3. Compose the native backend and helper-owned authorization/ownership persistence, including mount/unmount lifecycle.
4. Validate one-time boot, guided install, Windows preservation and cleanup in disposable UEFI/GPT VMs.
5. Implement authenticated completion evidence and read-back of the installed system.
6. Qualify Secure Boot and BitLocker behavior; do not silently suspend protection or disable security features.
7. Validate automation per release; Debian disk/bootloader steps currently remain interactive, and Mint remains guided.
8. Perform physical-hardware and performance qualification, then sign/package the production release.
