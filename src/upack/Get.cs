using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Inedo.UPack.CLI
{
    [DisplayName("get")]
    [Description("Downloads a universal package from a feed without installing it.")]
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)]
    internal sealed class Get : Command
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

        [DisplayName("target")]
        [Description("Directory where the package file will be saved.")]
        [ExtraArgument(Optional = true)]
        [ExpandPath]
        public string TargetDirectory { get; set; }

        [DisplayName("user")]
        [Description("User name and password to use for servers that require authentication. Example: \"«username»:«password»\" or \"api:«api-key»\"")]
        [ExtraArgument]
        [UseEnvironmentVariableAsDefault("UPACK_USER")]
        public NetworkCredential Authentication { get; set; }

        [DisplayName("overwrite")]
        [Description("When specified, overwrite files in the target directory.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool Overwrite { get; set; }

        [DisplayName("prerelease")]
        [Description("When version is not specified, will download the latest prerelase version instead of the latest stable version.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool Prerelease { get; set; }

        public override async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            var targetDirectory = this.TargetDirectory;
            if (string.IsNullOrEmpty(targetDirectory))
                targetDirectory = Environment.CurrentDirectory;

            var client = CreateClient(this.SourceUrl, this.Authentication);
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

            var fileName = Path.Combine(targetDirectory, $"{id.Name}-{version.Major}.{version.Minor}.{version.Patch}.upack");
            if (File.Exists(fileName) && !this.Overwrite)
                throw new UpackException($"File {fileName} already exists and --overwrite is not specified.");

            Console.WriteLine($"Saving package to {fileName}...");

            // use FileMode.Create/CreateNew here to guard against race condition with File.Exists
            using (var destStream = new FileStream(fileName, this.Overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var stream = await openPackageAsync())
            {
                stream.CopyTo(destStream);
            }

            Console.WriteLine("Package downloaded.");

            return 0;

            async Task<Stream> openPackageAsync()
            {
                try
                {
                    return await client.GetPackageStreamAsync(id, version, cancellationToken)
                        ?? throw new UpackException(PackageNotFoundMessage);
                }
                catch (WebException ex)
                {
                    throw ConvertWebException(ex, PackageNotFoundMessage);
                }
            }
        }
    }
}
