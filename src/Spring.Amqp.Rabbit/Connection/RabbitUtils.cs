using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Exceptions;
using System;

namespace Spring.Amqp.Rabbit.Connection
{
    public abstract class RabbitUtils
    {
        public const ushort AccessRefused = 403;
        public const ushort BasicClassId60 = 60;
        public const ushort ChannelProtocolClassId20 = 20;
        public const ushort ConnectionProtocolClassId10 = 10;
        public const ushort ConsumeMethodId20 = 20;
        public const ushort DeclareMethodId10 = 10;
        public const ushort ExchangeClassId40 = 40;
        public const ushort NotFound = 530;
        public const ushort QueueClassId50 = 50;
        public const ushort ReplySuccess = 200;

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

        public static bool IsExclusiveUseChannelClose(OperationInterruptedException e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            var reason = e.ShutdownReason;

            return reason.ReplyCode == AccessRefused &&
                   reason.ClassId == BasicClassId60 &&
                   reason.MethodId == ConsumeMethodId20 &&
                   reason.ReplyText.Contains("exclusive");
        }

        public static bool IsNormalChannelClose(OperationInterruptedException e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            return IsNormalShutdown(e);
        }

        public static bool IsNormalShutdown(OperationInterruptedException e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            var reason = e.ShutdownReason;

            return reason.ReplyCode == ReplySuccess && reason.ReplyText == "OK";
        }

        public static bool IsPassiveDeclarationChannelClose(OperationInterruptedException e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            var reason = e.ShutdownReason;

            return reason.ReplyCode == NotFound &&
                   reason.MethodId == DeclareMethodId10 &&
                   (reason.ClassId == ExchangeClassId40 || reason.ClassId == QueueClassId50);
        }
    }
}
