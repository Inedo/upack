using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Inedo.UPack.Packaging;

namespace Inedo.UPack.CLI
{
    [DisplayName("push")]
    [Description("Pushes a ProGet universal package to the specified ProGet feed.")]
    public sealed class Push : Command
    {
        [DisplayName("package")]
        [Description("Path of a valid .upack file.")]
        [PositionalArgument(0)]
        [ExpandPath]
        public string Package { get; set; }

        [DisplayName("target")]
        [Description("URL of a upack API endpoint.")]
        [PositionalArgument(1)]
        [UseEnvironmentVariableAsDefault("UPACK_FEED")]
        public string Target { get; set; }

        [DisplayName("user")]
        [Description("User name and password to use for servers that require authentication. Example: \"«username»:«password»\" or \"api:«api-key»\"")]
        [ExtraArgument]
        [UseEnvironmentVariableAsDefault("UPACK_USER")]
        public NetworkCredential Authentication { get; set; }

        public override async Task<int> RunAsync(CancellationToken cancellationToken)
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
                    throw new UpackException("The specified file is not a valid universal package: " + ex.Message, ex);
                }

                var error = ValidateManifest(info);
                if (error != null)
                {
                    Console.Error.WriteLine("Invalid upack.json: {0}", error);
                    return 2;
                }

                packageStream.Position = 0;

                var client = CreateClient(this.Target, this.Authentication);

                PrintManifest(info);

                try
                {
                    await client.UploadPackageAsync(packageStream, cancellationToken);
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
