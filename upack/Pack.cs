using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;

namespace Inedo.ProGet.UPack
{
    [DisplayName("pack")]
    [Description("Creates a new ProGet universal package using specified metadata and source directory.")]
    public sealed class Pack : Command
    {
        [DisplayName("metadata")]
        [Description("Path of a valid upack.json metadata file.")]
        [ExtraArgument]
        public string Manifest { get; set; }

        [DisplayName("source")]
        [Description("Directory containing files to add to the package.")]
        [PositionalArgument(0)]
        public string SourceDirectory { get; set; }

        [DisplayName("targetDirectory")]
        [Description("Directory where the .upack file will be created. If not specified, the current working directory is used.")]
        [ExtraArgument]
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


        public override async Task<int> RunAsync()
        {
            this.TargetDirectory = this.TargetDirectory ?? Environment.CurrentDirectory;

            PackageMetadata info;
            bool useMetadata = false;

            if (string.IsNullOrWhiteSpace(this.Manifest))
            {
                info = new PackageMetadata
                {
                    Group = this.Group,
                    Name = this.Name,
                    Version = this.Version,
                    Title = this.Title,
                    Description = this.PackageDescription,
                    IconUrl = this.IconUrl
                };
            }
            else
            {
                useMetadata = true;
                using (var metadataStream = File.OpenRead(this.Manifest))
                {
                    info = await ReadManifestAsync(metadataStream);
                }
            }

            if (string.IsNullOrEmpty(info.Name))
            {
                Console.Error.WriteLine("Missing package name.");
                return 2;
            }
            if (string.IsNullOrEmpty(info.Version))
            {
                Console.Error.WriteLine("Missing package version.");
                return 2;
            }

            PrintManifest(info);

            var serializer = new DataContractJsonSerializer(typeof(PackageMetadata));

            var fileName = Path.Combine(this.TargetDirectory, $"{info.Name}-{info.BareVersion}.upack");
            using (var zipStream = new FileStream(fileName, FileMode.Create, FileAccess.Write))
            using (var zipFile = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
            {
                if (useMetadata)
                    await CreateEntryFromFileAsync(zipFile, this.Manifest, "upack.json");
                else
                    await Task.Run(async () =>
                    {
                        using (var metadata = new MemoryStream())
                        {
                            serializer.WriteObject(metadata, info);
                            await CreateEntryFromStreamAsync(zipFile, metadata, "upack.json");
                        }
                    });
                
                await AddDirectoryAsync(zipFile, this.SourceDirectory, "package/");
            }

            return 0;
        }
    }
}
