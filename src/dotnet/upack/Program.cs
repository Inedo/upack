using System.Net;

namespace Inedo.UPack.CLI
{
    public sealed class Program
    {
        public static void Main(string[] args)
        {
            ServicePointManager.Expect100Continue = false;
            CommandDispatcher.Default.Main(args);
        }
    }
}
