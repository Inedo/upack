using System.ComponentModel;
using Inedo.UPack.Packaging;

namespace Inedo.UPack.CLI
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

        public override async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<RegisteredPackage> packages;
            using (var registry = PackageRegistry.GetRegistry(this.UserRegistry))
            {
                await registry.LockAsync(cancellationToken);
                try
                {
                    packages = await registry.GetInstalledPackagesAsync();
                }
                finally
                {
                    await registry.UnlockAsync();
                }
            }

            foreach (var pkg in packages)
            {
                if (!string.IsNullOrEmpty(pkg.Group))
                {
                    Console.WriteLine($"{pkg.Group}:{pkg.Name} {pkg.Version}");
                }
                else
                {
                    Console.WriteLine($"{pkg.Name} {pkg.Version}");
                }
                if (!string.IsNullOrEmpty(pkg.FeedUrl))
                {
                    Console.WriteLine($"From {pkg.FeedUrl}");
                }
                if (!string.IsNullOrEmpty(pkg.InstallPath) || pkg.InstallationDate != null)
                {
                    Console.WriteLine($"Installed to {(string.IsNullOrEmpty(pkg.InstallPath) ? "<unknown path>" : pkg.InstallPath)} on {(string.IsNullOrEmpty(pkg.InstallationDate) ? "<unknown date>" : pkg.InstallationDate)}");
                }
                if (!string.IsNullOrEmpty(pkg.InstalledBy) || !string.IsNullOrEmpty(pkg.InstalledUsing))
                {
                    Console.WriteLine($"Installed by {(string.IsNullOrEmpty(pkg.InstalledBy) ? "<unknown user>" : pkg.InstalledBy)} using {(string.IsNullOrEmpty(pkg.InstalledUsing) ? "<unknown application>" : pkg.InstalledUsing)}");
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
