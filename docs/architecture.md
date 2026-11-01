# Architecture and trust boundaries

`Orchester.App` owns the unelevated WPF shell and view model. `Orchester.Localization` owns two complete resource dictionaries. Runtime language changes update controls without restarting. `Orchester.Core` contains immutable snapshots, plans, manifests, phases and adapter contracts.

`Orchester.Storage` runs a fixed read-only Windows PowerShell script using the Windows Storage cmdlets, which wrap supported CIM storage APIs. Paths and strings from the UI are never inserted into the script. No diskpart, raw filesystem resizing, or writable disk handle is used. Access failures produce unknown encryption/shrink observations, which prevent planning. Inventory queries have a 30-second timeout; supported-host filesystem-aware safety/shrink queries have a separate 180-second budget because Windows may perform lengthy allocation analysis. Cancellation kills an incomplete discovery process. Unsupported hosts perform inventory only and retain unknown shrink/encryption observations. The UI does not elevate itself.

`Orchester.Images` verifies images using streaming SHA-256 and bounded ISO9660 reads. A partial download is specific to the expected digest and is always rehashed in full. Download results are atomically promoted only after hash validation; a mismatch discards the partial file. A server ignoring Range restarts at zero. Cancellation retains a resumable partial file. Publisher authenticity is a separate property from byte integrity; users must authenticate a reference digest independently. Bundled profiles contain no invented hashes. Automatic redirects are disabled to avoid security downgrades; use the bundled direct HTTPS origin.

`Orchester.Adapters` exposes Ubuntu, Debian and Mint implementations behind a common contract. This preview implements inspection and planning validation. Installation methods reject execution until real implementations and evidence exist. Catalog data cannot name executable code or shell commands.

`Orchester.Security` computes the SHA-256 fingerprint of the entire approved plan and independently rechecks disk identities, all partition metadata, protected types, shrink limits, encryption and geometry. Mutation requests are rejected regardless of UI settings. The implemented typed diagnostic helper uses a SID-restricted pipe, nonce and launched-server PID check. Mutations are not exposed through this endpoint. A future mutation endpoint must bind a protected, single-use authorization to the exact plan fingerprint inside the helper.

`Orchester.Engine` persists each journal entry with `Flush(true)` before replacing the manifest using a flushed temporary file. Journal/manifest divergence fails closed on load. Cancellation is only legal before storage execution; arbitrary partition rollback is never promised. Dry runs cannot transition into execution states. `COMPLETED` has no generic transition: a future evidence-specific completion method must validate actual installed-system and boot evidence.

`Orchester.Recovery` uses a separate allowlist projection for shared diagnostics. Stable disk identities become hashed aliases; local paths and credential material are excluded. Full manifests remain private local files. Firmware repair and cleanup require independent ownership and dependency verification before future implementation can enable them.


## Execution-services extension

The additional services and current trust boundaries are described in [execution-services.md](execution-services.md). Directory staging is a normal-user file operation; it is distinct from writing a staging partition. Its receipts are checked against the authenticated source image rather than accepted as trust anchors. The fixed native storage and firmware writers remain behind a compiled gate.

`Orchester.Helper` is a separate x64 executable requiring administrator consent. It creates a named pipe with an explicit ACL for the current Windows SID, permitting the same-user UAC elevation boundary; `CurrentUserOnly` alone also compares elevation level and is unsuitable for that boundary. The client verifies the pipe server PID against the process it launched. Requests carry a random nonce and a bounded typed schema. The helper accepts one diagnostic request, independently revalidates data, then exits. No executable/PowerShell command string, file path to run, or storage mutation command is accepted over this protocol. Unsigned preview binaries are development artifacts; production packaging and helper identity must be signed/pinned.

The privileged storage process uses the system PowerShell executable and restricts module resolution to the trusted Windows system module directory. Its command payload is JSON data passed over stdin. The operation kind and all resulting commands are fixed in source.

`ExecutionCoordinator` owns a separate ordered critical-action log and persisted plan. It never infers installation completion from that log. `InstallationRegistry` requires a trusted probe and independent completion evidence; neither a native installed-root probe nor an authenticated installer return transport is implemented yet.
