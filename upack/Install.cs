using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Inedo.ProGet.UPack
{
    [DisplayName("install")]
    [Description("Downloads the specified ProGet universal package and extracts its contents to a directory.")]
    public sealed class Install : Command
    {
        [DisplayName("package")]
        [Description("Package name and group, such as group:name.")]
        [PositionalArgument(0)]
        public string PackageName { get; set; }

        [DisplayName("version")]
        [Description("Package version. If not specified, the latest version is retrieved.")]
        [PositionalArgument(1, Optional = true)]
        public string Version { get; set; }

        [DisplayName("sourceUrl")]
        [Description("URL of a upack API endpoint.")]
        [ExtraArgument(Optional = false)]
        public string SourceUrl { get; set; }

        [DisplayName("target")]
        [Description("Directory where the contents of the package will be extracted.")]
        [ExtraArgument(Optional = false)]
        public string TargetDirectory { get; set; }

        [DisplayName("user")]
        [Description("User name and password to use for servers that require authentication. Example: username:password")]
        [ExtraArgument]
        public string Authentication { get; set; }

        [DisplayName("overwrite")]
        [Description("When specified, Overwrite files in the target directory.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool Overwrite { get; set; } = false;

        public override async Task<int> RunAsync()
        {
            throw new NotImplementedException();
        }
    }
}
