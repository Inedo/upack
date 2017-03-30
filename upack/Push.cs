using System;
using System.ComponentModel;
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
        public string Authentication { get; set; }

        public override async Task<int> RunAsync()
        {
            throw new NotImplementedException();
        }
    }
}
