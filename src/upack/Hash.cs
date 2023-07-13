using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Inedo.UPack.CLI
{
    [DisplayName("hash")]
    [Description("Calculates the SHA1 hash of a local package and writes it to standard output.")]
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)]
    internal sealed class Hash : Command
    {
        [DisplayName("package")]
        [Description("Path of a valid .upack file.")]
        [PositionalArgument(0)]
        [ExpandPath]
        public string PackagePath { get; set; }

        public override Task<int> RunAsync(CancellationToken cancellationToken)
        {
            var sha1 = GetSHA1(this.PackagePath);

            Console.WriteLine(sha1);

            return Task.FromResult(0);
        }
    }
}
