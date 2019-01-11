using System.Net;

namespace Inedo.ProGet.UPack
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
