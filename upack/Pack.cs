using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Inedo.UPack;
using Inedo.UPack.Packaging;

namespace Inedo.ProGet.UPack
{
    [DisplayName("pack")]
    [Description("Creates a new ProGet universal package using specified metadata and source directory.")]
    public sealed class Pack : Command
    {
        [DisplayName("manifest")]
        [AlternateName("metadata")]
        [Description("Path of a valid upack.json metadata file.")]
        [ExtraArgument]
        public string Manifest { get; set; }

        [DisplayName("source")]
        [Description("Directory containing files to add to the package.")]
        [PositionalArgument(0)]
        [ExpandPath]
        public string SourceDirectory { get; set; }

        [DisplayName("targetDirectory")]
        [Description("Directory where the .upack file will be created. If not specified, the current working directory is used.")]
        [ExtraArgument]
        [ExpandPath]
        public string TargetDirectory { get; set; }

        [DisplayName("group")]
        [Description("Package group. If metadata file is provided, value will be ignored.")]
        [ExtraArgument]
        public string Group { get; set; }

        [DisplayName("name")]
        [Description("Package name. If metadata file is provided, value will be ignored.")]
        [ExtraArgument]
        public string Name { get; set; }

        [DisplayName("version")]
        [Description("Package version. If metadata file is provided, value will be ignored.")]
        [ExtraArgument]
        public string Version { get; set; }

        [DisplayName("title")]
        [Description("Package title. If metadata file is provided, value will be ignored.")]
        [ExtraArgument]
        public string Title { get; set; }

        [DisplayName("description")]
        [Description("Package description. If metadata file is provided, value will be ignored.")]
        [ExtraArgument]
        public string PackageDescription { get; set; }

        [DisplayName("icon")]
        [Description("Icon absolute Url. If metadata file is provided, value will be ignored.")]
        [ExtraArgument]
        public string IconUrl { get; set; }

        public override async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            UniversalPackageMetadata info;

            if (string.IsNullOrWhiteSpace(this.Manifest))
            {
                info = new UniversalPackageMetadata
                {
                    Group = this.Group,
                    Name = this.Name,
                    Version = UniversalPackageVersion.TryParse(this.Version),
                    Title = this.Title,
                    Description = this.PackageDescription,
                    Icon = this.IconUrl
                };
            }
            else
            {
                if (!File.Exists(this.Manifest))
                {
                    Console.Error.WriteLine($"The manifest file '{this.Manifest}' does not exist.");
                    return 2;
                }

                using (var metadataStream = File.OpenRead(this.Manifest))
                {
                    info = await ReadManifestAsync(metadataStream);
                }
            }

            var error = ValidateManifest(info);
            if (error != null)
            {
                Console.Error.WriteLine("Invalid {0}: {1}", string.IsNullOrWhiteSpace(this.Manifest) ? "parameters" : "upack.json", error);
                return 2;
            }

            PrintManifest(info);

            if (!Directory.Exists(this.SourceDirectory))
            {
                Console.Error.WriteLine($"The source directory '{this.SourceDirectory}' does not exist.");
                return 2;
            }

            string relativePackageFileName = $"{info.Name}-{info.Version.Major}.{info.Version.Minor}.{info.Version.Patch}.upack";
            string targetFileName = Path.Combine(this.TargetDirectory ?? Environment.CurrentDirectory, relativePackageFileName);

            if (File.Exists(Path.Combine(this.SourceDirectory, relativePackageFileName)))
            {
                Console.Error.WriteLine("Warning: output file already exists in source directory and may be included inadvertently in the package contents.");
            }

            string tmpPath = Path.GetTempFileName();
            using (var builder = new UniversalPackageBuilder(tmpPath, info))
            {
                await builder.AddContentsAsync(
                    this.SourceDirectory, 
                    "/", 
                    true, 
                    s => string.IsNullOrWhiteSpace(this.Manifest) || !string.Equals(s, "upack.json", StringComparison.OrdinalIgnoreCase), 
                    cancellationToken
                );
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetFileName));
            File.Delete(targetFileName);
            File.Move(tmpPath, targetFileName);

            return 0;
        }
    }
}
