using System.ComponentModel;
using System.IO.Compression;
using Inedo.UPack.Packaging;

namespace Inedo.UPack.CLI
{
    [DisplayName("pack")]
    [Description("Creates a new universal package using specified metadata and source directory.")]
    public sealed class Pack : Command
    {
        [DisplayName("manifest")]
        [AlternateName("metadata")]
        [Description("Path of a valid upack.json metadata file.")]
        [ExtraArgument]
        [ExpandPath]
        public string Manifest { get; set; }

        [DisplayName("source")]
        [Description("File or directory containing files to add to the package.")]
        [PositionalArgument(0)]
        [ExpandPath]
        public string SourcePath { get; set; }

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

        [DisplayName("no-audit")]
        [Description("Do not store audit information in the UPack manifest.")]
        [ExtraArgument]
        public bool NoAudit { get; set; }

        [DisplayName("note")]
        [Description("A description of the purpose for creating this upack file.")]
        [ExtraArgument]
        public string Note { get; set; }

        [DisplayName("compression-level")]
        [Description("Compression level to use for files added to the upack. Valid values are optimal, fast, or none.")]
        [ExtraArgument]
        public string Compression { get; set; }

        public override async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            if (this.NoAudit && !string.IsNullOrEmpty(this.Note))
            {
                Console.Error.WriteLine("--no-audit cannot be used with --note.");
                return 2;
            }

            var compressionLevel = CompressionLevel.Optimal;
            if (!string.IsNullOrWhiteSpace(this.Compression))
            {
                CompressionLevel? level = this.Compression.ToLowerInvariant() switch
                {
                    "optimal" => CompressionLevel.Optimal,
                    "fast" or "fastest" => CompressionLevel.Fastest,
                    "none" or "nocompression" or "store" => CompressionLevel.NoCompression,
                    _ => null
                };

                if (!level.HasValue)
                {
                    Console.Error.WriteLine("Invalid value for --compression-level. Valid values are optimal, fast, or none.");
                    return 2;
                }

                compressionLevel = level.GetValueOrDefault();
            }

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

                using var metadataStream = File.OpenRead(this.Manifest);
                info = UniversalPackageMetadata.Parse(metadataStream);
            }

            var error = ValidateManifest(info);
            if (error != null)
            {
                Console.Error.WriteLine("Invalid {0}: {1}", string.IsNullOrWhiteSpace(this.Manifest) ? "parameters" : "upack.json", error);
                return 2;
            }

            PrintManifest(info);

            if (!this.NoAudit)
            {
                info["createdDate"] = DateTime.UtcNow.ToString("u");
                if (!string.IsNullOrEmpty(this.Note))
                {
                    info["createdReason"] = this.Note;
                }
                info["createdUsing"] = "upack/" + typeof(Pack).Assembly.GetName().Version.ToString(3);
                info["createdBy"] = Environment.UserName;
            }

            if (!Directory.Exists(this.SourcePath) && !File.Exists(this.SourcePath))
            {
                Console.Error.WriteLine($"The source directory '{this.SourcePath}' does not exist.");
                return 2;
            }

            string relativePackageFileName = $"{info.Name}-{info.Version.Major}.{info.Version.Minor}.{info.Version.Patch}.upack";
            string targetFileName = Path.Combine(this.TargetDirectory ?? Environment.CurrentDirectory, relativePackageFileName);

            if (File.Exists(Path.Combine(this.SourcePath, relativePackageFileName)))
            {
                Console.Error.WriteLine("Warning: output file already exists in source directory and may be included inadvertently in the package contents.");
            }

            string tmpPath = Path.GetTempFileName();
            using (var builder = new UniversalPackageBuilder(tmpPath, info))
            {
                if (Directory.Exists(this.SourcePath))
                {
                    await builder.AddContentsAsync(
                        this.SourcePath,
                        "/",
                        true,
                        compressionLevel,
                        s => string.IsNullOrWhiteSpace(this.Manifest) || !string.Equals(s, "upack.json", StringComparison.OrdinalIgnoreCase),
                        cancellationToken
                    );
                }
                else
                {
                    using var file = File.Open(this.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await builder.AddFileAsync(file, Path.GetFileName(this.SourcePath), File.GetLastWriteTimeUtc(this.SourcePath), compressionLevel, cancellationToken);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetFileName));
            File.Delete(targetFileName);
            File.Move(tmpPath, targetFileName);

            return 0;
        }
    }
}
