using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Inedo.UPack.CLI
{
    [DisplayName("version")]
    [Description("Outputs the installed version of upack.")]
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)]
    internal class Version : Command
    {
        public override Task<int> RunAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine(typeof(Program).Assembly.GetName().Version);
            return Task.FromResult(0);
        }
    }
}