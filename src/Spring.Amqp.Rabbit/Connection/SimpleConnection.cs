using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using RabbitMQ.Client.Framing.Impl;
using Spring.Amqp.Rabbit.Support;
using System;

namespace Spring.Amqp.Rabbit.Connection
{
    /// <summary>
    /// Simply an <see cref="IConnection"/>.
    /// </summary>
    public class SimpleConnection : IConnection, NetworkConnection
    {
        private readonly int _closeTimeout;

        private bool _disposed;

        public SimpleConnection(RabbitMQ.Client.IConnection connectionDelegate, int closeTimeout)
        {
            DelegateConnection = connectionDelegate ?? throw new ArgumentNullException(nameof(connectionDelegate));
            _closeTimeout = closeTimeout;
        }

        public event EventHandler<ConnectionBlockedEventArgs> ConnectionBlocked
        {
            add => DelegateConnection.ConnectionBlocked += value;
            remove => DelegateConnection.ConnectionBlocked -= value;
        }

        public RabbitMQ.Client.IConnection DelegateConnection { get; }

        public int LocalPort => DelegateConnection is NetworkConnection ? DelegateConnection.LocalPort : 0;

        public int RemotePort => DelegateConnection.RemotePort;

        public IModel CreateChannel(bool transactional)
        {
            try
            {
                var channel = DelegateConnection.CreateModel();
                if (channel == null)
                    throw new AmqpResourceNotAvailableException("The channelMax limit is reached. Try later.");

                if (transactional) channel.TxSelect();

                return channel;
            }
            catch (Exception ex)
            {
                throw RabbitExceptionTranslator.ConvertRabbitAccessException(ex);
            }
        }

        public bool IsOpen()
        {
            if (!_disposed && DelegateConnection is AutorecoveringConnection && !DelegateConnection.IsOpen)
                throw new AutoRecoverConnectionNotCurrentlyOpenException("Auto recovery connection is not currently open.");

            return DelegateConnection != null && DelegateConnection.IsOpen;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public override string ToString()
        {
            return $"{nameof(SimpleConnection)}@{this.GetIdentityHexString()} " +
                $"[delegate={DelegateConnection}, local port={LocalPort}]";
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            try
            {
                _disposed = true;
                DelegateConnection.Close(_closeTimeout);
                DelegateConnection.Dispose();
            }
            catch (AlreadyClosedException)
            {
            }
            catch (Exception ex)
            {
                throw RabbitExceptionTranslator.ConvertRabbitAccessException(ex);
            }
        }
    }
}
