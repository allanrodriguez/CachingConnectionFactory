using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Spring.Amqp.Rabbit.Connection
{
    public abstract class AbstractConnectionFactory : IConnectionFactory, IDisposable
    {
        private const string BadUri = "Uri was passed an invalid URI; it is ignored";
        
        private readonly object _lock = new object();

        private AbstractConnectionFactory _publisherConnectionFactory;
        private IList<AmqpTcpEndpoint> _addresses;
        private bool _disposed;

        public AbstractConnectionFactory(ConnectionFactory rabbitConnectionFactory)
        {
            RabbitConnectionFactory = rabbitConnectionFactory ??
                throw new ArgumentNullException(nameof(rabbitConnectionFactory),
                    "Target ConnectionFactory must not be null");

            using var loggerFactory = new LoggerFactory();

            Logger = loggerFactory.CreateLogger(GetType());
        }

        #region Events

        public event EventHandler<ChannelCreatedEventArgs> ChannelCreated
        {
            add
            {
                lock (_lock)
                {
                    ChannelCreatedInternal += value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ChannelCreated += value;
                }
            }
            remove
            {
                lock (_lock)
                {
                    ChannelCreatedInternal -= value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ChannelCreated -= value;
                }
            }
        }

        public event EventHandler<OperationInterruptedException> ChannelShutDown
        {
            add
            {
                lock (_lock)
                {
                    ChannelShutDownInternal += value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ChannelShutDown += value;
                }
            }
            remove
            {
                lock (_lock)
                {
                    ChannelShutDownInternal -= value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ChannelShutDown -= value;
                }
            }
        }

        public event EventHandler<IConnection> ConnectionCreated
        {
            add
            {
                lock (_lock)
                {
                    ConnectionCreatedInternal += value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ConnectionCreated += value;
                }
            }
            remove
            {
                lock (_lock)
                {
                    ConnectionCreatedInternal -= value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ConnectionCreated -= value;
                }
            }
        }

        public event EventHandler<IConnection> ConnectionClosed
        {
            add
            {
                lock (_lock)
                {
                    ConnectionClosedInternal += value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ConnectionClosed += value;
                }
            }
            remove
            {
                lock (_lock)
                {
                    ConnectionClosedInternal -= value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ConnectionClosed -= value;
                }
            }
        }

        public event EventHandler<OperationInterruptedException> ConnectionShutDown
        {
            add
            {
                lock (_lock)
                {
                    ConnectionShutDownInternal += value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ConnectionShutDown += value;
                }
            }
            remove
            {
                lock (_lock)
                {
                    ConnectionShutDownInternal -= value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ConnectionShutDown -= value;
                }
            }
        }

        private event EventHandler<ChannelCreatedEventArgs> ChannelCreatedInternal;
        private event EventHandler<OperationInterruptedException> ChannelShutDownInternal;
        private event EventHandler<IConnection> ConnectionCreatedInternal;
        private event EventHandler<IConnection> ConnectionClosedInternal;
        private event EventHandler<OperationInterruptedException> ConnectionShutDownInternal;

        #endregion

        #region Properties

        public ConnectionFactory RabbitConnectionFactory { get; }

        protected ILogger Logger { get; } = LoggerFactory.Create(builder => builder.AddConsole())
                                                         .CreateLogger<AbstractConnectionFactory>();

        public string Host
        {
            get => RabbitConnectionFactory.HostName;
            set => RabbitConnectionFactory.HostName = value;
        }

        public int Port
        {
            get => RabbitConnectionFactory.Port;
            set => RabbitConnectionFactory.Port = value;
        }

        public string VirtualHost
        {
            get => RabbitConnectionFactory.VirtualHost;
            set => RabbitConnectionFactory.VirtualHost = value;
        }

        public string UserName
        {
            get => RabbitConnectionFactory.UserName;
            set => RabbitConnectionFactory.UserName = value;
        }

        public Uri Uri
        {
            get => RabbitConnectionFactory.Uri;
            set
            {
                try
                {
                    RabbitConnectionFactory.Uri = value;
                }
                catch (ArgumentException ex)
                {
                    Logger.LogInformation(ex, BadUri);
                }
            }
        }

        public ushort RequestedHeartbeat
        {
            get => RabbitConnectionFactory.RequestedHeartbeat;
            set => RabbitConnectionFactory.RequestedHeartbeat = value;
        }

        public int RequestedConnectionTimeout
        {
            get => RabbitConnectionFactory.RequestedConnectionTimeout;
            set => RabbitConnectionFactory.RequestedConnectionTimeout = value;
        }

        public IConnectionFactory PublisherConnectionFactory => _publisherConnectionFactory;

        public bool IsSimplePublisherConfirms => false;

        public bool IsPublisherConfirms => false;

        public bool IsPublisherReturns => false;

        #endregion

        public void SetChannelCreatedHandlers(IEnumerable<EventHandler<ChannelCreatedEventArgs>> handlers)
        {
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));

            ClearChannelCreatedHandlers();

            foreach (var handler in handlers) ChannelCreated += handler;
        }

        public void ClearChannelCreatedHandlers()
        {
            lock (_lock)
            {
                ChannelCreatedInternal = null;

                if (_publisherConnectionFactory != null) _publisherConnectionFactory.ClearChannelCreatedHandlers();
            }
        }

        public void SetChannelShutDownHandlers(IEnumerable<EventHandler<OperationInterruptedException>> handlers)
        {
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));

            ClearChannelShutDownHandlers();

            foreach (var handler in handlers) ChannelShutDown += handler;
        }

        public void ClearChannelShutDownHandlers()
        {
            lock (_lock)
            {
                ChannelShutDownInternal = null;

                if (_publisherConnectionFactory != null) _publisherConnectionFactory.ClearChannelShutDownHandlers();
            }
        }

        public void SetConnectionCreatedHandlers(IEnumerable<EventHandler<IConnection>> handlers)
        {
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));

            ClearConnectionCreatedHandlers();

            foreach (var handler in handlers) ConnectionCreated += handler;
        }

        public void ClearConnectionCreatedHandlers()
        {
            lock (_lock)
            {
                ConnectionCreatedInternal = null;

                if (_publisherConnectionFactory != null) _publisherConnectionFactory.ClearConnectionCreatedHandlers();
            }
        }

        public void SetConnectionClosedHandlers(IEnumerable<EventHandler<IConnection>> handlers)
        {
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));

            ClearConnectionClosedHandlers();

            foreach (var handler in handlers) ConnectionClosed += handler;
        }

        public void ClearConnectionClosedHandlers()
        {
            lock (_lock)
            {
                ConnectionClosedInternal = null;

                if (_publisherConnectionFactory != null) _publisherConnectionFactory.ClearConnectionClosedHandlers();
            }
        }

        public void SetConnectionShutDownHandlers(IEnumerable<EventHandler<OperationInterruptedException>> handlers)
        {
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));

            ClearConnectionShutDownHandlers();

            foreach (var handler in handlers) ConnectionShutDown += handler;
        }

        public void ClearConnectionShutDownHandlers()
        {
            lock (_lock)
            {
                ConnectionShutDownInternal = null;

                if (_publisherConnectionFactory != null) _publisherConnectionFactory.ClearConnectionShutDownHandlers();
            }
        }

        public IConnection CreateConnection()
        {
            throw new NotImplementedException();
        }

        public void SetPassword(string password)
        {
            RabbitConnectionFactory.Password = password;
        }

        public void SetAddresses(string addresses)
        {
            if (!string.IsNullOrWhiteSpace(addresses))
            {
                var addressArray = AmqpTcpEndpoint.ParseMultiple(addresses);
                if (addressArray.Length > 0)
                {
                    _addresses = new List<AmqpTcpEndpoint>(addressArray);

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.SetAddresses(addresses);

                    return;
                }
            }

            Logger.LogInformation("SetAddresses() called with an empty value, will be using the host+port properties for connections");
            _addresses = null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected string GetDefaultHostName()
        {
            string temp;

            try
            {
                temp = Dns.GetHostName();
                Logger.LogDebug("Using hostname [{temp}] for hostname.", temp);
            }
            catch (SocketException ex)
            {
                Logger.LogWarning(ex, "Could not get host name, using 'localhost' as default value");
                temp = "localhost";
            }

            return temp;
        }

        protected void SetPublisherConnectionFactory(AbstractConnectionFactory publisherConnectionFactory)
        {
            _publisherConnectionFactory = publisherConnectionFactory;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing && _publisherConnectionFactory != null) _publisherConnectionFactory.Dispose();

            _disposed = true;
        }
    }
}
