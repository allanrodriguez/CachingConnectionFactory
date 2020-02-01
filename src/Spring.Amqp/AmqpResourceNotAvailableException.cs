using System;

namespace Spring.Amqp
{
    /// <summary>
    /// The <see cref="AmqpException"/> thrown when some resource can't be accessed.
    /// </summary>
    public class AmqpResourceNotAvailableException : AmqpException
    {
        public AmqpResourceNotAvailableException()
        {
        }

        public AmqpResourceNotAvailableException(string message) : base(message)
        {
        }

        public AmqpResourceNotAvailableException(Exception innerException) : base(innerException?.ToString())
        {
        }

        public AmqpResourceNotAvailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
