namespace Inedo.UPack.CLI
{
    internal sealed class UpackException : Exception
    {
        public UpackException()
        {
        }

        public UpackException(string message)
            : base(message)
        {
        }

        public UpackException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
