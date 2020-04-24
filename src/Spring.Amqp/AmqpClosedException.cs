using System;

namespace Spring.Amqp
{
    public class AmqpClosedException : AmqpException
    {
        public AmqpClosedException()
        {
        }

        public AmqpClosedException(string message) : base(message)
        {
        }

        public AmqpClosedException(Exception innerException) : base(innerException)
        {
        }

        public AmqpClosedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
