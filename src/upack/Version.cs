using System.ComponentModel;

namespace Inedo.UPack.CLI
{
    [DisplayName("version")]
    [Description("Outputs the installed version of upack.")]
    public class Version : Command
    {
        public override Task<int> RunAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine(typeof(Program).Assembly.GetName().Version);
            return Task.FromResult(0);
        }
    }
}