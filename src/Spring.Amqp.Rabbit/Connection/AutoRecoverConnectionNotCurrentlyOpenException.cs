namespace Spring.Amqp.Rabbit.Connection
{
    /// <summary>
    /// An exception thrown if the connection is an auto recover connection that is not currently open and is in the
    /// process of being recovered.
    /// </summary>
    public class AutoRecoverConnectionNotCurrentlyOpenException : AmqpException
    {
        public AutoRecoverConnectionNotCurrentlyOpenException(string message) : base(message)
        {
        }

        public AutoRecoverConnectionNotCurrentlyOpenException()
        {
        }

        public AutoRecoverConnectionNotCurrentlyOpenException(string message, System.Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
