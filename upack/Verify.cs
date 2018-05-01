using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Inedo.UPack;
using Inedo.UPack.Packaging;

namespace Inedo.ProGet.UPack
{
    [DisplayName("verify")]
    [Description("Verifies that a specified package hash matches the hash stored in a ProGet Universal feed.")]
    public sealed class Verify : Command
    {
        [DisplayName("package")]
        [Description("Path of a valid .upack file.")]
        [PositionalArgument(0)]
        [ExpandPath]
        public string PackagePath { get; set; }

        [DisplayName("source")]
        [Description("URL of a upack API endpoint.")]
        [PositionalArgument(1)]
        public string SourceEndpoint { get; set; }

        [DisplayName("user")]
        [Description("User name and password to use for servers that require authentication. Example: username:password")]
        [ExtraArgument]
        public NetworkCredential Authentication { get; set; }

        public override async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            var metadata = GetPackageMetadata();
            var packageId = new UniversalPackageId(metadata.Group, metadata.Name);
            var client = CreateClient(this.SourceEndpoint, this.Authentication);
            var remoteVersion = await client.GetPackageVersionAsync(packageId, metadata.Version, false, cancellationToken);

            if (remoteVersion == null)
                throw new UpackException($"Package {packageId} was not found in feed.");

            var sha1 = GetSHA1(this.PackagePath);

            if (sha1 != remoteVersion.SHA1)
                throw new UpackException($"Package SHA1 value {sha1} did not match remote SHA1 value {remoteVersion.SHA1}");

            Console.WriteLine("Hashes for local and remote package match: " + sha1);

            return 0;
        }

        private UniversalPackageMetadata GetPackageMetadata()
        {
            try
            {
                using (var package = new UniversalPackage(this.PackagePath))
                {
                    return package.GetFullMetadata().Clone();
                }
            }
            catch (Exception ex)
            {
                throw new UpackException($"The source package file '{this.PackagePath}' does not exist or could not be opened.", ex);
            }
        }
    }
}
