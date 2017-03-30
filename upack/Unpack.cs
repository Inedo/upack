using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Inedo.ProGet.UPack
{
    [DisplayName("unpack")]
    [Description("Extracts the contents of a ProGet universal package to a directory.")]
    public sealed class Unpack : Command
    {
        [DisplayName("package")]
        [Description("Path of a valid .upack file.")]
        [PositionalArgument(0)]
        public string Package { get; set; }

        [DisplayName("target")]
        [Description("Directory where the contents of the package will be extracted.")]
        [PositionalArgument(1)]
        public string Target { get; set; }

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
