namespace Inedo.UPack.CLI
{
    public sealed class Program
    {
        public static Task<int> Main(string[] args) => CommandDispatcher.Default.MainAsync(args);
    }
}
