using System;

namespace Inedo.UPack.CLI
{
    public sealed class UpackException : Exception
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
