using System;

namespace Spring.Amqp
{
    /// <summary>
    /// Base <see cref="Exception"/> for errors that occur when executing AMQP operations.
    /// </summary>
    public class AmqpException : Exception
    {
        public AmqpException()
        {
        }

        public AmqpException(string message) : base(message)
        {
        }

        public AmqpException(Exception innerException) : base(innerException?.ToString())
        {
        }

        public AmqpException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
