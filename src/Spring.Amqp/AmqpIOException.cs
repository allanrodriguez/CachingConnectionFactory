using System;
using System.IO;

namespace Spring.Amqp
{
    /// <summary>
    /// <see cref="Exception"/> wrapper for an <see cref="IOException"/> which can be commonly thrown from AMQP
    /// operations.
    /// </summary>
    public class AmqpIOException : AmqpException
    {
        public AmqpIOException()
        {
        }

        public AmqpIOException(string message) : base(message)
        {
        }

        public AmqpIOException(IOException innerException) : base(innerException)
        {
        }

        public AmqpIOException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
