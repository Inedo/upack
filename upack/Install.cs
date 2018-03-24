using Inedo.UPack;
using Inedo.UPack.Packaging;
using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

namespace Inedo.ProGet.UPack
{
    [DisplayName("install")]
    [Description("Downloads the specified ProGet universal package and extracts its contents to a directory.")]
    public sealed class Install : Command
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
        [Description("URL of a upack API endpoint.")]
        [ExtraArgument(Optional = false)]
        public string SourceUrl { get; set; }

        [DisplayName("target")]
        [Description("Directory where the contents of the package will be extracted.")]
        [ExtraArgument(Optional = true)]
        public string TargetDirectory { get; set; }

        [DisplayName("user")]
        [Description("User name and password to use for servers that require authentication. Example: username:password")]
        [ExtraArgument]
        public NetworkCredential Authentication { get; set; }

        [DisplayName("overwrite")]
        [Description("When specified, Overwrite files in the target directory.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool Overwrite { get; set; } = false;

        [DisplayName("prerelease")]
        [Description("When version is not specified, will install the latest prerelase version instead of the latest stable version.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool Prerelease { get; set; } = false;

        [DisplayName("comment")]
        [Description("The reason for installing the package, for the local registry.")]
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

        public override async Task<int> RunAsync()
        {
            var targetDirectory = this.TargetDirectory;
            if (String.IsNullOrEmpty(targetDirectory))
                targetDirectory = Environment.CurrentDirectory;

            var client = CreateClient(this.SourceUrl, this.Authentication);
            UniversalPackageId id;
            try
            {
                id = UniversalPackageId.Parse(this.PackageName);
            }
            catch (ArgumentException ex)
            {
                throw new ApplicationException("Invalid package ID: " + ex.Message, ex);
            }
            var version = await GetVersionAsync(client, id, this.Version, this.Prerelease);
            
            Stream stream = null;
            if (!this.Unregistered)
            {
                using (var registry = PackageRegistry.GetRegistry(this.UserRegistry))
                {
                    await registry.LockAsync();
                    try
                    {
                        if (this.CachePackages)
                        {
                            stream = await registry.TryOpenFromCacheAsync(id, version);
                        }

                        await registry.RegisterPackageAsync(new RegisteredPackage
                        {
                            FeedUrl = this.SourceUrl,
                            Group = id.Group,
                            Name = id.Name,
                            Version = version.ToString(),
                            InstallPath = this.TargetDirectory,
                            InstallationDate = DateTimeOffset.Now.ToString("o"),
                            InstallationReason = this.Comment,
                            InstalledBy = Environment.UserName,
                            InstalledUsing = Assembly.GetEntryAssembly().GetName().Name + "/" + Assembly.GetEntryAssembly().GetName().Version.ToString()
                        });
                    }
                    finally
                    {
                        await registry.UnlockAsync();
                    }
                }
            }

            if (stream == null)
            {
                try
                {
                    stream = await client.GetPackageStreamAsync(id, version);
                }
                catch (WebException ex)
                {
                    throw ConvertWebException(ex, PackageNotFoundMessage);
                }
            }

            using (var package = new UniversalPackage(stream))
            {
                await UnpackZipAsync(this.TargetDirectory, this.Overwrite, package, this.PreserveTimestamps);
            }

            return 0;
        }
    }
}
