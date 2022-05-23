using System.ComponentModel;
using System.Net;
using System.Text.Json;

namespace Inedo.UPack.CLI
{
    [DisplayName("metadata")]
    [Description("Displays metadata for a remote universal package.")]
    public sealed class Metadata : Command
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
        [UseEnvironmentVariableAsDefault("UPACK_FEED")]
        public string SourceUrl { get; set; }

        [DisplayName("user")]
        [Description("User name and password to use for servers that require authentication. Example: \"�username�:�password�\" or \"api:�api-key�\"")]
        [ExtraArgument]
        [UseEnvironmentVariableAsDefault("UPACK_USER")]
        public NetworkCredential Authentication { get; set; }

        [DisplayName("file")]
        [Description("The metadata file to display relative to the .upack root; the default is upack.json.")]
        [ExtraArgument]
        public string FilePath { get; set; }

        public override async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            var client = CreateClient(this.SourceUrl, this.Authentication);

            UniversalPackageId packageId;
            try
            {
                packageId = UniversalPackageId.Parse(this.PackageName);
            }
            catch (ArgumentException ex)
            {
                throw new UpackException("Invalid package ID: " + ex.Message, ex);
            }

            UniversalPackageVersion version = null;
            if (!string.IsNullOrEmpty(this.Version))
            {
                version = UniversalPackageVersion.TryParse(this.Version);
                if (version == null)
                    throw new UpackException($"Invalid UPack version number: {this.Version}");
            }

            using var stream = await client.GetPackageFileStreamAsync(packageId, version, string.IsNullOrEmpty(this.FilePath) ? "upack.json" : this.FilePath, cancellationToken);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            foreach (var p in doc.RootElement.EnumerateObject())
                Console.WriteLine($"{p.Name} = {p.Value}");

            return 0;
        }
    }
}
