using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Inedo.UPack.CLI
{
    [DisplayName("remove")]
    [Description("Remove the specified universal package from directory.")]
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)]
    internal class Remove : Command
    {
        [DisplayName("package")]
        [Description("Package name and group, such as group/name.")]
        [PositionalArgument(0)]
        public string PackageName { get; set; }


        [DisplayName("target")]
        [Description("(Optional) Directory where the package to be removed is located.")]
        [ExtraArgument(Optional = true)]
        [ExpandPath]
        public string TargetDirectory { get; set; }

        [DisplayName("userregistry")]
        [Description("Use this if you registered the package installation using the user registry instead of the machine registry.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool UserRegistry { get; set; } = false;

        [DisplayName("rmregistry")]
        [Description("Use this if you need to remove a remaining registry from a package that has been deleted without using the tool. "
            + "The command will still check if the package exists to remove it as well.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool RemoveRegistry { get; set; }

        public override async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            var targetDirectory = this.TargetDirectory;
            if (string.IsNullOrEmpty(targetDirectory))
                targetDirectory = Environment.CurrentDirectory;

            bool removed = await RemoveAsync(targetDirectory, this.PackageName, this.UserRegistry, this.RemoveRegistry, cancellationToken);

            if (removed)
            {            
                Console.WriteLine($"The package '{PackageName}' was removed successfully");
            }
            else
            { 
                Console.WriteLine($"Package registry was removed with sucess.");
            }

            return 0;
        }
    }
}
