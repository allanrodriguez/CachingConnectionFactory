using System;

namespace Spring.Amqp
{
    /// <summary>
    /// Runtime wrapper for an authentication exception.
    /// </summary>
    public class AmqpAuthenticationException : AmqpException
    {
        public AmqpAuthenticationException()
        {
        }

        public AmqpAuthenticationException(string message) : base(message)
        {
        }

        public AmqpAuthenticationException(Exception innerException) : base(innerException)
        {
        }

        public AmqpAuthenticationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
