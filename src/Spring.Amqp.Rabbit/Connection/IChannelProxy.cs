using RabbitMQ.Client;

namespace Spring.Amqp.Rabbit.Connection
{
    public interface IChannelProxy : IModel
    {
        /// <summary>
        /// True if confirms are selected on this channel.
        /// </summary>
        bool IsConfirmSelected { get; }

        /// <summary>
        /// Whether this channel has transactions enabled.
        /// </summary>
        bool IsTransactional { get; }

        /// <summary>
        /// The target Channel of this proxy.
        /// </summary>
        IModel TargetChannel { get; }
    }
}
