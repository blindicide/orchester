# Windows integration checks

Build and run the app on disposable Windows UEFI/GPT VMs. Compare `Get-Disk`, `Get-Partition`, `Get-Volume`, firmware boot entries and EFI file hashes before and after the full foundation workflow. They must remain unchanged. Verify unknown permissions, BitLocker states and unavailable shrink limits block planning. Confirm live en-US/ru-RU switching, keyboard navigation, Windows Narrator, multi-monitor DPI, dark/light appearance, cancel/resume, journal truncation recovery and diagnostic redaction.

The portable automated harness does not substitute for these Windows-specific checks. This folder intentionally contains no destructive scripts.
