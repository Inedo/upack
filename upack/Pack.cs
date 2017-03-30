using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Inedo.ProGet.UPack
{
    [DisplayName("pack")]
    [Description("Creates a new ProGet universal package using specified metadata and source directory.")]
    public sealed class Pack : Command
    {
        [DisplayName("metadata")]
        [Description("Path of a valid upack.json metadata file.")]
        [PositionalArgument(0)]
        public string Manifest { get; set; }

        [DisplayName("source")]
        [Description("Directory containing files to add to the package.")]
        [PositionalArgument(1)]
        public string SourceDirectory { get; set; }

        [DisplayName("targetDirectory")]
        [Description("Directory where the .upack file will be created. If not specified, the current working directory is used.")]
        [ExtraArgument]
        public string TargetDirectory { get; set; }

        public override async Task<int> RunAsync()
        {
            this.TargetDirectory = this.TargetDirectory ?? Environment.CurrentDirectory;

            PackageMetadata info;

            using (var metadataStream = File.OpenRead(this.Manifest))
            {
                info = await ReadManifestAsync(metadataStream);
            }

            PrintManifest(info);

            var fileName = Path.Combine(this.TargetDirectory, $"{info.Name}-{info.Version}.upack");
            using (var zipStream = new FileStream(fileName, FileMode.Create, FileAccess.Write))
            using (var zipFile = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
            {
                await CreateEntryFromFileAsync(zipFile, this.Manifest, "upack.json");
                await AddDirectoryAsync(zipFile, this.SourceDirectory, "package/");
            }

            return 0;
        }
    }
}
