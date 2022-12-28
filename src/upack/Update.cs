using Inedo.UPack.Packaging;
using System.ComponentModel;
using System.Net;

namespace Inedo.UPack.CLI
{
    [DisplayName("update")]
    [Description("Performs a clean update of the specified universal package.")]
    internal class Update : Command
    {
        [DisplayName("package")]
        [Description("Package name and group, such as group/name.")]
        [PositionalArgument(0)]
        public string PackageName { get; set; }

        [DisplayName("version")]
        [Description("Package version. If not specified, the latest version is retrieved.")]
        [PositionalArgument(1, Optional = true)]
        public string Version { get; set; }


        [DisplayName("source")]
        [Description("(Optional) URL of a upack API endpoint.")]
        [ExtraArgument(Optional = true)]
        [UseEnvironmentVariableAsDefault("UPACK_FEED")]
        public string SourceUrl { get; set; }

        [DisplayName("target")]
        [Description("(Optional) Directory where the package is installed.")]
        [ExtraArgument(Optional = true)]
        [ExpandPath]
        public string TargetDirectory { get; set; }

        [DisplayName("user")]
        [Description("User name and password to use for servers that require authentication. Example: \"«username»:«password»\" or \"api:«api-key»\"")]
        [ExtraArgument]
        [UseEnvironmentVariableAsDefault("UPACK_USER")]
        public NetworkCredential Authentication { get; set; }

        [DisplayName("prerelease")]
        [Description("When version is not specified, will install the latest prerelase version instead of the latest stable version.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool Prerelease { get; set; } = false;

        [DisplayName("comment")]
        [Description("The reason for updating the package, for the local registry.")]
        [ExtraArgument]
        public string Comment { get; set; }

        [DisplayName("userregistry")]
        [Description("Register the package in the user registry instead of the machine registry.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool UserRegistry { get; set; } = false;

        [DisplayName("unregistered")]
        [Description("Do not register the package in a local registry.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool Unregistered { get; set; } = false;

        [DisplayName("cache")]
        [Description("Cache the contents of the package in the local registry.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool CachePackages { get; set; } = false;

        [DisplayName("preserve-timestamps")]
        [Description("Set extracted file timestamps to the timestamp of the file in the archive instead of the current time.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool PreserveTimestamps { get; set; } = false;

        [DisplayName("force")]
        [Description("Force the update even if it's already up-to-date.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool Force { get; set; } = false;

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
            var installedPackageRegistry = packages.FirstOrDefault(p => p.Name == PackageName);

            var targetDirectory = this.TargetDirectory;
            if (string.IsNullOrEmpty(targetDirectory))
            {
                if (installedPackageRegistry != null)
                    targetDirectory = installedPackageRegistry.InstallPath;

                if (string.IsNullOrEmpty(targetDirectory))
                    targetDirectory = Environment.CurrentDirectory;
            }
                


            string sourceUrl = this.SourceUrl;
            if (string.IsNullOrEmpty(sourceUrl))
            {
                try
                {
                    sourceUrl = installedPackageRegistry.FeedUrl;
                }
                catch
                {
                    throw new UpackException("Package registry not found! \n" +
                        "If it's installed, consider declaring the source url.");
                }
            }
              
            var client = CreateClient(sourceUrl, this.Authentication);
            UniversalPackageId id;
            try
            {
                id = UniversalPackageId.Parse(this.PackageName);
            }
            catch (ArgumentException ex)
            {
                throw new UpackException("Invalid package ID: " + ex.Message, ex);
            }

            var version = await GetVersionAsync(client, id, this.Version, this.Prerelease, cancellationToken);

            // Compare versions
            if (!Force)
            {
                var parsedInstalledVersion = UniversalPackageVersion.TryParse(installedPackageRegistry.Version);
                if (version == parsedInstalledVersion)
                {
                    Console.WriteLine("The version of installed and the new package are the same!");
                    return 0;
                }
                else if (version < parsedInstalledVersion)
                {
                    Console.WriteLine($"The version of package to be installed ({version}) is older than the installed package ({parsedInstalledVersion}).");
                    return 0;
                }
            }

            // Remove files to make a clean update
            bool removed = false;
            removed = await RemoveAsync(targetDirectory, this.PackageName, this.UserRegistry, false, cancellationToken);
            if (!removed)
                return 0;

            using (var package = new UniversalPackage(await openPackageAsync()))
            {
                id = new UniversalPackageId(package.Group, package.Name);
                version = package.Version;
                await UnpackZipAsync(targetDirectory, false, package, this.PreserveTimestamps, cancellationToken);
            }

            if (!this.Unregistered)
            {
                using (var registry = PackageRegistry.GetRegistry(this.UserRegistry))
                {
                    await registry.LockAsync(cancellationToken);
                    await registry.RegisterPackageAsync(
                        new RegisteredPackage
                        {
                            FeedUrl = sourceUrl,
                            Group = id.Group,
                            Name = id.Name,
                            Version = version.ToString(),
                            InstallPath = targetDirectory,
                            InstallationDate = DateTimeOffset.Now.ToString("o"),
                            InstallationReason = this.Comment,
                            InstalledBy = Environment.UserName,
                            InstalledUsing = "upack/" + typeof(Program).Assembly.GetName().Version.ToString()
                        }
                    );
                }
            }

            Console.WriteLine($"Updated {PackageName} from {installedPackageRegistry.Version} to {version}");

            return 0;

            async Task<Stream> openPackageAsync()
            {
                using (var registry = PackageRegistry.GetRegistry(this.UserRegistry))
                {
                    if (this.CachePackages)
                    {
                        var s = await registry.TryOpenFromCacheAsync(id, version, cancellationToken);
                        if (s != null)
                            return s;
                    }

                    try
                    {
                        var s = await client.GetPackageStreamAsync(id, version, cancellationToken);
                        if (s == null)
                            throw new UpackException(PackageNotFoundMessage);

                        if (this.CachePackages)
                        {
                            await registry.WriteToCacheAsync(id, version, s, cancellationToken);
                            s.Dispose();
                            return await registry.TryOpenFromCacheAsync(id, version, cancellationToken);
                        }

                        return s;
                    }
                    catch (WebException ex)
                    {
                        throw ConvertWebException(ex, PackageNotFoundMessage);
                    }
                }
            }
        }
    }
}
