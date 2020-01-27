using System;

namespace Spring.Amqp
{
    /// <summary>
    /// Exception thrown when some time-bound operation fails to execute in the desired time.
    /// </summary>
    public class AmqpTimeoutException : AmqpException
    {
        public AmqpTimeoutException()
        {
        }

        public AmqpTimeoutException(string message) : base(message)
        {
        }

        public AmqpTimeoutException(Exception innerException) : base(innerException)
        {
        }

        public AmqpTimeoutException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
