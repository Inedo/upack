using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Inedo.UPack.CLI
{
    [DisplayName("verify")]
    [Description("Verifies that a specified package hash matches the hash stored in a universal feed.")]
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)]
    internal sealed class Verify : Command
    {
        [DisplayName("package")]
        [Description("Path of a valid .upack file.")]
        [PositionalArgument(0)]
        [ExpandPath]
        public string PackagePath { get; set; }

        [DisplayName("source")]
        [Description("URL of a upack API endpoint.")]
        [PositionalArgument(1)]
        [UseEnvironmentVariableAsDefault("UPACK_FEED")]
        public string SourceEndpoint { get; set; }

        [DisplayName("user")]
        [Description("User name and password to use for servers that require authentication. Example: \"«username»:«password»\" or \"api:«api-key»\"")]
        [ExtraArgument]
        [UseEnvironmentVariableAsDefault("UPACK_USER")]
        public NetworkCredential Authentication { get; set; }

        public override async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            var metadata = GetPackageMetadata(this.PackagePath);
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
    }
}
