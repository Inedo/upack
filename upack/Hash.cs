using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Inedo.UPack;
using Inedo.UPack.Packaging;

namespace Inedo.ProGet.UPack
{
    [DisplayName("hash")]
    [Description("Calculates the SHA1 hash of a local package and writes it to standard output.")]
    public sealed class Hash : Command
    {
        [DisplayName("package")]
        [Description("Path of a valid .upack file.")]
        [PositionalArgument(0)]
        [ExpandPath]
        public string PackagePath { get; set; }

        public override Task<int> RunAsync(CancellationToken cancellationToken)
        {
            var metadata = GetPackageMetadata();
            var packageId = new UniversalPackageId(metadata.Group, metadata.Name);

            var sha1 = GetSHA1(this.PackagePath);

            Console.WriteLine(sha1);

            return Task.FromResult(0);
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
