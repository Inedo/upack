using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Net;
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
        [ExtraArgument(Optional = false)]
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
            using (var stream = await this.OpenPackageAsync())
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                await UnpackZipAsync(this.TargetDirectory, this.Overwrite, zip, this.PreserveTimestamps);
            }

            return 0;
        }

        private async Task<Stream> OpenPackageAsync()
        {
            var r = this.Unregistered ? Registry.Unregistered : this.UserRegistry ? Registry.User : Registry.Machine;
            string group = null, name = null, version = null;

            var parts = this.PackageName.Split(new[] { ':', '/' });
            group = parts.Length > 1 ? string.Join("/", new ArraySegment<string>(parts, 0, parts.Length - 1)) : null;
            name = parts[parts.Length - 1];

            version = await GetVersionAsync(this.SourceUrl, group, name, this.Version, this.Authentication, this.Prerelease);

            await r.RegisterPackageAsync(group, name, UniversalPackageVersion.Parse(version),
                this.TargetDirectory, this.SourceUrl, this.Authentication,
                this.Comment, null, Environment.UserName);

            return await r.GetOrDownloadAsync(group, name, UniversalPackageVersion.Parse(version), this.SourceUrl, this.Authentication, this.CachePackages);
        }
    }
}
