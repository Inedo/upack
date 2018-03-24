using Inedo.UPack.Packaging;
using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace Inedo.ProGet.UPack
{
    [DisplayName("push")]
    [Description("Pushes a ProGet universal package to the specified ProGet feed.")]
    public sealed class Push : Command
    {
        [DisplayName("package")]
        [Description("Path of a valid .upack file.")]
        [PositionalArgument(0)]
        public string Package { get; set; }

        [DisplayName("target")]
        [Description("URL of a upack API endpoint.")]
        [PositionalArgument(1)]
        public string Target { get; set; }

        [DisplayName("user")]
        [Description("User name and password to use for servers that require authentication. Example: username:password")]
        [ExtraArgument]
        public NetworkCredential Authentication { get; set; }

        public override async Task<int> RunAsync()
        {
            using (var packageStream = new FileStream(this.Package, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous))
            {
                UniversalPackageMetadata info;

                try
                {
                    using (var package = new UniversalPackage(packageStream, true))
                    {
                        info = package.GetFullMetadata();
                    }
                }
                catch (Exception ex)
                {
                    throw new ApplicationException("The specified file is not a valid universal package: " + ex.Message, ex);
                }

                packageStream.Position = 0;

                var client = CreateClient(this.Target, this.Authentication);

                PrintManifest(info);

                try
                {
                    await client.UploadPackageAsync(packageStream);
                }
                catch (WebException ex)
                {
                    throw ConvertWebException(ex);
                }

                if (!string.IsNullOrEmpty(info.Group))
                    Console.WriteLine($"{info.Group}:{info.Name} {info.Version} published!");
                else
                    Console.WriteLine($"{info.Name} {info.Version} published!");
            }

            return 0;
        }
    }
}
