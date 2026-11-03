using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orchester.Core;
namespace Orchester.Adapters;

public static class InstallerRecipes {
    public static InstallerRecipe Create(ReleaseProfile profile) {
        var adapter = DistributionAdapter.For(profile); adapter.ValidateRelease(profile, false);
        return adapter.Id switch {
            "ubuntu" or "mint" => new(profile.Id, BootStrategy.CasperIso, profile.KernelPath, profile.InitrdPath,
                "/orchester/source.iso", ["boot=casper", "iso-scan/filename=/orchester/source.iso", "---"], false),
            "debian" => new(profile.Id, BootStrategy.DebianHdMedia, "/hd-media/vmlinuz", "/hd-media/initrd.gz",
                "/orchester/source.iso", ["iso-scan/filename=/orchester/source.iso", "priority=high", "---"], true),
            _ => throw new SafetyException("AdapterMismatch")
        };
    }
    public static string GrubPreview(ReleaseProfile profile) {
        var recipe = Create(profile);
        // All commands and arguments are compiled here, never read as executable catalog data.
        if (recipe.RequiresAdditionalHdMedia) return "# Debian requires independently authenticated matching hd-media kernel/initrd.\n# DVD initrd must not be substituted.\n";
        return "# PREVIEW: not a validated or signed boot chain\nset timeout=10\nmenuentry 'ORCHESTER installer' {\n" +
            "  search --no-floppy --file --set=root /orchester/source.iso\n" +
            "  loopback loop /orchester/source.iso\n" +
            $"  linux (loop){SafeIsoPath(recipe.KernelPath)} {string.Join(' ', recipe.KernelArguments)}\n" +
            $"  initrd (loop){SafeIsoPath(recipe.InitrdPath)}\n}}\n";
    }
    private static string SafeIsoPath(string path) {
        if (!Regex.IsMatch(path, @"\A/[a-zA-Z0-9/_.-]+\z") || path.Contains("..")) throw new SafetyException("UnsafePath");
        return path;
    }
}

public static class UnattendedGenerator {
    private static readonly Regex Name = new(@"\A[a-z][a-z0-9-]{0,30}\z");
    public static GeneratedConfiguration Generate(ReleaseProfile profile, InstallationPlan plan, UnattendedOptions options) {
        DistributionAdapter.For(profile).ValidateRelease(profile, false);
        if (plan.ProfileId != profile.Id || plan.OperationId == Guid.Empty) throw new SafetyException("AdapterMismatch");
        Validate(options, plan);
        return profile.Id.StartsWith("ubuntu-", StringComparison.Ordinal) ? Ubuntu(options) :
            profile.Id.StartsWith("debian-", StringComparison.Ordinal) ? Debian(options) : throw new SafetyException("AutomationUnavailable");
    }
    private static void Validate(UnattendedOptions o, InstallationPlan plan) {
        if (!Name.IsMatch(o.Hostname) || !Name.IsMatch(o.Username) || o.Username is "root" or "daemon" or "nobody" ||
            !Regex.IsMatch(o.Locale, @"\A[a-z]{2}_[A-Z]{2}(?:\.UTF-8)?\z") || !Regex.IsMatch(o.Keyboard, @"\A[a-z]{2}\z") ||
            !Regex.IsMatch(o.TimeZone, @"\A[A-Za-z_+-]+(?:/[A-Za-z0-9_+-]+){0,2}\z")) throw new SafetyException("InvalidInstallerOptions");
        // A strong existing crypt(3) SHA-512 hash only. Plaintext passwords cannot enter this API.
        if (!Regex.IsMatch(o.PasswordHash, @"\A\$6\$rounds=[1-9][0-9]{5,8}\$[./A-Za-z0-9]{16}\$[./A-Za-z0-9]{85}[./01]\z"))
            throw new SafetyException("PasswordHashRequired");
        var t = o.Target;
        if (t.RootId == Guid.Empty || t.EfiId == Guid.Empty || t.RootId == t.EfiId || t.RootNumber < 1 || t.EfiNumber < 1 || t.RootNumber == t.EfiNumber ||
            !t.DedicatedEfiOwned || !t.RootFormatApproved ||
            !Regex.IsMatch(t.LinuxDiskSerial, @"\A[A-Za-z0-9_.:-]{4,128}\z") ||
            plan.OriginalDisk.Partitions.Any(p => p.Id == t.RootId || p.Id == t.EfiId)) throw new SafetyException("AutomationTargetUnsafe");
        if (o.Packages.IsDefault || o.Packages.Any(p => !Regex.IsMatch(p, @"\A[a-z0-9][a-z0-9+.-]{0,100}\z")))
            throw new SafetyException("InvalidInstallerOptions");
    }
    private static GeneratedConfiguration Ubuntu(UnattendedOptions o) {
        var t = o.Target;
        // Existing, independently provisioned partitions only. No disk-wide "use all space" directive.
        object[] storage = [
            new { id="target",type="disk",serial=t.LinuxDiskSerial,ptable="gpt",preserve=true,grub_device=false },
            new { id="root",type="partition",device="target",number=t.RootNumber,preserve=true },
            new { id="root-format",type="format",volume="root",fstype="ext4",preserve=false },
            new { id="root-mount",type="mount",device="root-format",path="/" },
            new { id="linux-efi",type="partition",device="target",number=t.EfiNumber,preserve=true,grub_device=true },
            new { id="efi-format",type="format",volume="linux-efi",fstype="fat32",preserve=true },
            new { id="efi-mount",type="mount",device="efi-format",path="/boot/efi" }
        ];
        var payload = new { autoinstall = new { version=1, locale=o.Locale, keyboard=new { layout=o.Keyboard }, timezone=o.TimeZone,
            identity=new { hostname=o.Hostname, username=o.Username, password=o.PasswordHash },
            ssh=new { install_server=false, allow_pw=false }, packages=o.Packages, storage=new { config=storage }, shutdown="poweroff" } };
        return new("user-data", "#cloud-config\n" + JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented=true }) + "\n", true,
            ["AutomationPreview", "IndependentLinuxTargetRequired"]);
    }
    private static GeneratedConfiguration Debian(UnattendedOptions o) {
        // Debian Installer cannot safely express the already provisioned GUID-targeted layout here.
        // Leave partitioning and bootloader selection interactive instead of generating unsafe partman defaults.
        var text = new StringBuilder();
        void Line(string key, string type, string value) => text.Append("d-i ").Append(key).Append(' ').Append(type).Append(' ').Append(value).Append('\n');
        Line("debian-installer/locale", "string", o.Locale); Line("keyboard-configuration/xkb-keymap", "select", o.Keyboard);
        Line("time/zone", "string", o.TimeZone); Line("netcfg/get_hostname", "string", o.Hostname);
        Line("passwd/root-login", "boolean", "false"); Line("passwd/user-fullname", "string", o.Username);
        Line("passwd/username", "string", o.Username); Line("passwd/user-password-crypted", "password", o.PasswordHash);
        Line("mirror/country", "string", "manual"); Line("mirror/http/hostname", "string", "deb.debian.org");
        Line("mirror/http/directory", "string", "/debian");
        if (!o.Packages.IsEmpty) Line("pkgsel/include", "string", string.Join(' ', o.Packages));
        text.Append("# Partitioning and bootloader selection MUST remain interactive.\n# No partman confirmation, disk auto-selection or grub default is preseeded.\n");
        return new("preseed.cfg", text.ToString(), true, ["AutomationPreview", "DebianPartitioningInteractive"]);
    }
}
