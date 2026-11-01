# Distribution profiles

Bundled Ubuntu 24.04.3 Desktop/Server, Debian 13.0.0 DVD and Mint 22.2 Cinnamon/MATE/Xfce entries are **inspection candidates**. URLs and kernel/initrd layouts are candidate metadata and may require revision. All entries have `validated: false`; no advertised release has passed installation qualification. Official download availability is not guaranteed forever. Mint Secure Boot is conservatively unqualified.

Before enabling a release, record its exact ISO SHA-256 and publisher signature evidence, installer version and layout, EFI chain, installed bootloader behavior, locale coverage, automated configuration behavior, staging filesystem/file-size requirements and required kernel parameters. Run every VM case in the matrix, including preservation and cleanup. An Ubuntu-family name does not confer compatibility.

Ubuntu requires a release-specific Subiquity profile before autoinstall configuration can be generated. Debian requires a Debian Installer/preseed implementation including mirror, debconf, disk GUID targeting and bootloader behavior. Mint must remain guided until each edition's actual installer is independently validated. Current adapters explicitly reject preparation, boot configuration and unattended generation.

Remote catalog payloads must pass detached RSA-PSS/SHA-256 verification using a publisher key pinned through a separately trusted release process. The signed payload is schema-checked before use. This preview ships no publisher key or remote update service and does not accept third-party adapter code.


## Preparation recipes and configuration previews

Compiled recipes now distinguish Casper ISO boot from Debian hd-media boot. `PrepareInstaller` returns declarative recipe metadata; `GenerateBootConfiguration` returns a clearly labelled GRUB preview. Neither method installs a trusted bootloader. `UnattendedGenerator` produces preview-only Ubuntu cloud-config and Debian preseed with strict field/hash validation. Debian's disk and bootloader steps intentionally remain interactive, and Mint automation is rejected. See [execution-services.md](execution-services.md) for target/credential constraints. All release validation gates remain open.
