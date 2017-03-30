using System;
using System.ComponentModel;
using System.IO;
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
        public string TargetDirectory { get; set; } = Directory.GetCurrentDirectory();

        public override async Task<int> RunAsync()
        {
            throw new NotImplementedException();
        }
    }
}
