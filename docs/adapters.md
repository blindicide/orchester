# Distribution profiles

The embedded catalog supplies candidate Ubuntu Desktop/Server, Debian DVD/netinst and Linux Mint Cinnamon/MATE/Xfce releases. All carry `validated: false`. They are inspection/guided-preparation candidates, not advertised installation-qualified releases. Native helper policy reads its embedded catalog rather than trusting an editable adjacent JSON file.

Ubuntu/Mint use release-specific Casper kernel/initrd paths and a verified source ISO on the owned data partition. Debian uses dedicated matching hd-media kernel/initrd, authenticated separately and bound into the plan fingerprint. Its data staging is FAT32; oversized DVD images are refused, so use a compatible netinst image. Each candidate must actually contain its declared EFI and installer layout or preparation refuses before shrinking.

Secure Boot execution requires a qualified profile plus current firmware trust/revocation/SBAT verification. No bundled candidate currently meets that release qualification flag. Firmware security is never silently changed.

Guided installation is the executable mode. Ubuntu autoinstall and Debian preseed generation are preview services; they do not authorize live automatic partitioning. Mint remains guided-only. An Ubuntu-family name alone does not confer compatibility.

Qualifying a release means recording the exact image/authentication evidence, installer/layout, EFI chain, staging filesystem limits, boot parameters and actual preservation/cleanup behavior on Windows. Marking a profile qualified is a compiled release-policy update, not a UI risk override.

Signed remote catalog validation services accept data only; no downloaded adapter code or arbitrary installation script is executed. Remote publisher keys and hosted distribution are not provisioned in this repository.
