using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Exceptions;
using System;

namespace Spring.Amqp.Rabbit.Connection
{
    public abstract class RabbitUtils
    {
        public const ushort ChannelProtocolClassId20 = 20;
        public const ushort ConnectionProtocolClassId10 = 10;

        private static readonly ILogger<RabbitUtils> Logger = LogFactory.GetLogger<RabbitUtils>();

        /// <summary>
        /// Close the given RabbitMQ Connection and ignore any thrown exception. This is useful for typical
        /// <see langword="finally"/> blocks in manual RabbitMQ code.
        /// </summary>
        /// <param name="connection">The RabbitMQ Connection to close (may be null).</param>
        public static void CloseConnection(IConnection connection)
        {
            if (connection == null) return;

            try
            {
                connection.Dispose();
            }
            catch (AlreadyClosedException)
            {
            }
            catch (Exception e)
            {
                Logger.LogDebug(e, "Ignoring Connection exception - assuming already closed.");
            }
        }
    }
}
