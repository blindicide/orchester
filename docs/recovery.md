# Recovery

In the foundation preview, operations affect downloaded files and local manifests only. Disk layout and firmware boot entries are never modified.

- Cancel hashing/download with the Cancel operation button. Partial downloads can be resumed with the same destination and authenticated digest. A changed digest uses a different partial file.
- Cancel an approved dry run before execution to append a cancellation phase. Manifests are retained for diagnostics.
- If an application or machine stops between a flushed journal entry and manifest replacement, Recovery reports an inconsistent operation. Preserve its operation directory; do not treat it as completed or automatically delete resources based on it.
- Export redacted diagnostics from Recovery. Full manifests include local paths and stable identities; do not share them indiscriminately.
- A `PLAN_APPROVED` record is not an installed OS. A reboot or a saved record cannot certify a Linux installation.

For future releases, a storage write interruption must enter `RECOVERY_REQUIRED` or `UNKNOWN_EXTERNAL_STATE`. Re-enumerate disk identities and partitions and independently verify ORCHESTER ownership before cleanup. A resource must never be considered owned because it is FAT32, Linux-formatted, adjacent to another partition, or named similarly. Windows and third-party boot repair requires a separately approved procedure.

BitLocker: maintain external access to recovery credentials before future boot/storage operations. This preview does not collect, log, suspend, restore or decrypt encryption keys or protection. Unknown, locked, protected and suspended states block plans.


## Execution-service recovery

An execution-service attempt has `plan.json` and `execution.jsonl`, protected from concurrent writers by an exclusive operation lease. `ExecutionCoordinator.InspectRecoveryAsync` checks sequence/order/phase/fingerprint consistency. Missing or truncated data, an unverified critical intent, or a fabricated completion phase requires investigation. Existing execution directories are never blindly replayed.

Directory bundles contain a `preparing` marker until their receipt is flushed. Preserve incomplete bundles for analysis; choose a new operation rather than silently resuming them. Receipt edits cannot authorize altered kernel/initrd/EFI bytes because verification compares them with the authenticated source ISO. Reparse points and unknown extra files are refused.

Firmware cleanup requires the exact stored load-option hash, operation UUID, partition GUID and EFI path. A changed entry or persistent boot-order reference blocks deletion. A foreign one-time boot request is preserved. The native implementation remains gated pending qualification. These policies do not repair or remove unrelated Windows/Linux bootloaders.
