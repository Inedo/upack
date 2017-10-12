using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Inedo.ProGet.UPack
{
    [DisplayName("list")]
    [Description("Lists packages installed in the local registry.")]
    public sealed class List : Command
    {
        [DisplayName("userregistry")]
        [Description("List packages in the user registry instead of the machine registry.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool UserRegistry { get; set; } = false;

        public override async Task<int> RunAsync()
        {
            var packages = await (this.UserRegistry ? Registry.User : Registry.Machine).ListInstalledPackagesAsync();

            foreach (var pkg in packages)
            {
                Console.WriteLine($"{pkg.GroupAndName} {pkg.Version}");
                if (!string.IsNullOrEmpty(pkg.FeedUrl))
                {
                    Console.WriteLine($"From {pkg.FeedUrl}");
                }
                if (!string.IsNullOrEmpty(pkg.Path) || pkg.InstallationDate.HasValue)
                {
                    Console.WriteLine($"Installed to {pkg.Path ?? "<unknown path>"} on {pkg.InstallationDate?.ToString() ?? "<unknown date>"}");
                }
                if (!string.IsNullOrEmpty(pkg.InstalledBy) || !string.IsNullOrEmpty(pkg.InstalledUsing))
                {
                    Console.WriteLine($"Installed by {pkg.InstalledBy ?? "<unknown user>"} using {pkg.InstalledUsing ?? "<unknown application>"}");
                }
                if (!string.IsNullOrEmpty(pkg.InstallationReason))
                {
                    Console.WriteLine($"Comment: {pkg.InstallationReason}");
                }
                Console.WriteLine();
            }

            Console.WriteLine($"{packages.Count} packages");

            return 0;
        }
    }
}
