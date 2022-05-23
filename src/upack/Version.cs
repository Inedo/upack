using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace Inedo.UPack.CLI
{
    [DisplayName("version")]
    [Description("Outputs the installed version of upack.")]
    public class Version : Command
    {
        public override Task<int> RunAsync(CancellationToken cancellationToken)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            var version = fvi.FileVersion;

            Console.WriteLine(version);

            return Task.FromResult(0);
        }
    }
}