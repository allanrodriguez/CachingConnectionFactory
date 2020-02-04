using System;

namespace Spring.Amqp
{
    /// <summary>
    /// A "catch-all" exception type within the <see cref="AmqpException"/> hierarchy when no more specific cause is
    /// known.
    /// </summary>
    public class UncategorizedAmqpException : AmqpException
    {
        public UncategorizedAmqpException()
        {
        }

        public UncategorizedAmqpException(string message) : base(message)
        {
        }

        public UncategorizedAmqpException(Exception innerException) : base(innerException)
        {
        }

        public UncategorizedAmqpException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
