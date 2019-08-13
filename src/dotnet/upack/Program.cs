using System.Net;

namespace Inedo.UPack.CLI
{
    public sealed class Program
    {
        public static void Main(string[] args)
        {
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
            CommandDispatcher.Default.Main(args);
        }
    }
}
