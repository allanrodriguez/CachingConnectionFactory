using System;

namespace Spring.Amqp
{
    /// <summary>
    /// <see cref="Exception"/> wrapper for an <see cref="OperationInterruptedException"/> which can be commonly thrown
    /// from AMQP operations if the remote process dies or there is a network issue.
    /// </summary>
    public class AmqpConnectException : AmqpException
    {
        public AmqpConnectException()
        {
        }

        public AmqpConnectException(string message) : base(message)
        {
        }

        public AmqpConnectException(Exception innerException) : base(innerException)
        {
        }

        public AmqpConnectException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
