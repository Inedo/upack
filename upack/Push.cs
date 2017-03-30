using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
            using (var packageStream = new FileStream(this.Package, FileMode.Open, FileAccess.Read))
            {
                PackageMetadata info;

                using (var zipFile = new ZipArchive(packageStream, ZipArchiveMode.Read, true))
                {
                    var entry = zipFile.GetEntry("upack.json");
                    using (var metadataStream = entry.Open())
                    {
                        info = await ReadManifestAsync(metadataStream);
                    }
                }

                packageStream.Position = 0;

                PrintManifest(info);

                using (var client = CreateClient(this.Authentication))
                {
                    using (var response = await client.PutAsync(this.Target, new StreamContent(packageStream)
                    {
                        Headers =
                        {
                            ContentType = new MediaTypeHeaderValue("application/octet-stream")
                        }
                    }))
                    {
                        response.EnsureSuccessStatusCode();
                        Console.WriteLine($"{info.Group}:{info.Name} {info.Version} published!");
                    }
                }
            }

            return 0;
        }
    }
}
