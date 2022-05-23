using System.ComponentModel;
using Inedo.UPack.Packaging;

namespace Inedo.UPack.CLI
{
    [DisplayName("unpack")]
    [Description("Extracts the contents of a universal package to a directory.")]
    public sealed class Unpack : Command
    {
        [DisplayName("package")]
        [Description("Path of a valid .upack file.")]
        [PositionalArgument(0)]
        public string Package { get; set; }

        [DisplayName("target")]
        [Description("Directory where the contents of the package will be extracted.")]
        [PositionalArgument(1)]
        [ExpandPath]
        public string Target { get; set; }

        [DisplayName("overwrite")]
        [Description("When specified, overwrite files in the target directory.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool Overwrite { get; set; } = false;

        [DisplayName("preserve-timestamps")]
        [Description("Set extracted file timestamps to the timestamp of the file in the archive instead of the current time.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool PreserveTimestamps { get; set; } = false;

        public override async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            UniversalPackage package;
            try
            {
                package = new UniversalPackage(this.Package);
            }
            catch (Exception ex)
            {
                throw new UpackException("The specified file is not a valid universal package: " + ex.Message, ex);
            }

            using (package)
            {
                var info = package.GetFullMetadata();
                PrintManifest(info);

                await UnpackZipAsync(this.Target, this.Overwrite, package, this.PreserveTimestamps, cancellationToken);
            }

            return 0;
        }
    }
}
