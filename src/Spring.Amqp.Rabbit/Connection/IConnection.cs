using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;

namespace Spring.Amqp.Rabbit.Connection
{
    public interface IConnection : IDisposable
    {
        event EventHandler<ConnectionBlockedEventArgs> ConnectionBlocked;
        event EventHandler<EventArgs> ConnectionUnblocked;

        /// <summary>
        /// The local port if the underlying connection supports it.
        /// </summary>
        int LocalPort { get; }

        /// <summary>
        /// Returns the underlying RabbitMQ connection.
        /// </summary>
        RabbitMQ.Client.IConnection DelegateConnection { get; }

        /// <summary>
        /// Creates a new channel, using an internally allocated channel number.
        /// </summary>
        /// <param name="transactional">True if the channel should support transactions.</param>
        /// <returns>A new channel descriptor, or null if none is available.</returns>
        /// <exception cref="AmqpException">Thrown if an I/O problem is encountered.</exception>
        IModel CreateChannel(bool transactional);

        /// <summary>
        /// Flag to indicate the status of the connection.
        /// </summary>
        bool IsOpen();
    }
}
