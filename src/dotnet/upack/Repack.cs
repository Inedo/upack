using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inedo.UPack.Packaging;
using Newtonsoft.Json.Linq;

namespace Inedo.UPack.CLI
{
    [DisplayName("repack")]
    [Description("Creates a new ProGet universal package by repackaging an existing package with a new version number and audit information.")]
    public sealed class Repack : Command
    {
        [Obsolete]
        [AlternateName("manifest")]
        [AlternateName("metadata")]
        [ExtraArgument]
        [ExpandPath]
        public string Manifest { get; set; }

        [DisplayName("source")]
        [Description("The path of the existing upack file.")]
        [PositionalArgument(0)]
        [ExpandPath]
        public string SourcePath { get; set; }

        [DisplayName("targetDirectory")]
        [Description("Directory where the .upack file will be created. If not specified, the current working directory is used.")]
        [ExtraArgument]
        [ExpandPath]
        public string TargetDirectory { get; set; }

        [Obsolete]
        [AlternateName("group")]
        [ExtraArgument]
        public string Group { get; set; }

        [Obsolete]
        [AlternateName("name")]
        [ExtraArgument]
        public string Name { get; set; }

        [DisplayName("newVersion")]
        [AlternateName("version")]
        [Description("New package version to use.")]
        [ExtraArgument]
        public string NewVersion { get; set; }

        [Obsolete]
        [AlternateName("title")]
        [ExtraArgument]
        public string Title { get; set; }

        [Obsolete]
        [AlternateName("description")]
        [ExtraArgument]
        public string PackageDescription { get; set; }

        [Obsolete]
        [AlternateName("icon")]
        [ExtraArgument]
        public string IconUrl { get; set; }

        [Obsolete]
        [AlternateName("no-audit")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool NoAudit { get; set; }

        [DisplayName("note")]
        [Description("A description of the purpose for repackaging that will be entered as the audit note.")]
        [ExtraArgument]
        public string Note { get; set; }

        [DisplayName("overwrite")]
        [Description("Overwrite existing package file if it already exists.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool Overwrite { get; set; }

#pragma warning disable CS0612 // Type or member is obsolete
        public override async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            if (this.NoAudit && !string.IsNullOrEmpty(this.Note))
            {
                Console.Error.WriteLine("--no-audit cannot be used with --note.");
                return 2;
            }

            var info = GetPackageMetadata(this.SourcePath);
            var infoToMerge = await GetMetadataToMergeAsync();
            var hash = GetSHA1(this.SourcePath);

            var id = (string.IsNullOrEmpty(info.Group) ? "" : info.Group + "/") + info.Name + ":" + info.Version + ":" + hash;

            foreach (var modifiedProperty in infoToMerge)
                info[modifiedProperty.Key] = modifiedProperty.Value;

            var error = ValidateManifest(info);
            if (error != null)
            {
                Console.Error.WriteLine("Invalid {0}: {1}", string.IsNullOrWhiteSpace(this.Manifest) ? "parameters" : "upack.json", error);
                return 2;
            }

            PrintManifest(info);

            if (!this.NoAudit)
            {
                JArray history;
                if (info.ContainsKey("repackageHistory"))
                {
                    history = (JArray)info["repackageHistory"];
                }
                else
                {
                    history = new JArray();
                    info["repackageHistory"] = history;
                }

                var entry = new Dictionary<string, object>
                {
                    { "id", id },
                    { "date", DateTime.UtcNow.ToString("u") },
                    { "using", "upack/" + typeof(Repack).Assembly.GetName().Version.ToString(3) },
                    { "by", Environment.UserName }
                };

                if (!string.IsNullOrEmpty(this.Note))
                {
                    entry["reason"] = this.Note;
                }

                history.Add(JObject.FromObject(entry));
            }

            string relativePackageFileName = $"{info.Name}-{info.Version.Major}.{info.Version.Minor}.{info.Version.Patch}.upack";
            string targetFileName = Path.Combine(this.TargetDirectory ?? Environment.CurrentDirectory, relativePackageFileName);

            if (!this.Overwrite && File.Exists(targetFileName))
                throw new UpackException($"Target file '{targetFileName}' exists and overwrite was set to false.");

            string tmpPath = Path.GetTempFileName();

            using (var existingPackage = new UniversalPackage(this.SourcePath))
            using (var builder = new UniversalPackageBuilder(tmpPath, info))
            {
                var entries = from e in existingPackage.Entries
                              where !string.Equals(e.RawPath, "upack.json", StringComparison.OrdinalIgnoreCase)
                              select e;

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (entry.IsDirectory)
                    {
                        builder.AddEmptyDirectoryRaw(entry.RawPath);
                    }
                    else
                    {
                        using (var stream = entry.Open())
                        {
                            await builder.AddFileRawAsync(stream, entry.RawPath, entry.Timestamp, cancellationToken);
                        }
                    }
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetFileName));
            File.Delete(targetFileName);
            File.Move(tmpPath, targetFileName);

            return 0;
        }

        private async Task<UniversalPackageMetadata> GetMetadataToMergeAsync()
        {
            if (string.IsNullOrWhiteSpace(this.Manifest))
            {
                return new UniversalPackageMetadata
                {
                    Group = this.Group,
                    Name = this.Name,
                    Version = UniversalPackageVersion.TryParse(this.NewVersion),
                    Title = this.Title,
                    Description = this.PackageDescription,
                    Icon = this.IconUrl
                };
            }
            else
            {
                try
                {
                    using (var metadataStream = File.OpenRead(this.Manifest))
                    {
                        return await ReadManifestAsync(metadataStream);
                    }
                }
                catch (Exception ex)
                {
                    throw new UpackException($"The manifest file '{this.Manifest}' does not exist or could not be opened.", ex);
                }
            }
        }
#pragma warning restore CS0612 // Type or member is obsolete
    }
}
