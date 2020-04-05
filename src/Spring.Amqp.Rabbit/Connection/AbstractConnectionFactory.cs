using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Framing.Impl;
using Spring.Amqp.Rabbit.Support;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Spring.Amqp.Rabbit.Connection
{
    public abstract class AbstractConnectionFactory : IConnectionFactory, IDisposable
    {
        public const int DefaultCloseTimeout = 30000;

        private const string BadUri = "Uri was passed an invalid URI; it is ignored";
        private const string PublisherSuffix = ".publish";

        private readonly object _lock = new object();

        private IList<AmqpTcpEndpoint> _addresses;
        private int _closeTimeout = DefaultCloseTimeout;
        private ConnectionNameStrategy _connectionNameStrategy;
        private int _defaultConnectionNameStrategyCounter;
        private bool _disposed;
        private AbstractConnectionFactory _publisherConnectionFactory;
        private bool _shuffleAddresses;

        public AbstractConnectionFactory(ConnectionFactory rabbitConnectionFactory)
        {
            _connectionNameStrategy =
                connectionFactory =>
                    $"SpringAMQP#{this.GetIdentityHexString()}:{Interlocked.Increment(ref _defaultConnectionNameStrategyCounter) - 1}";

            RecoverySucceededInternal += (sender, e) =>
            {
                if (Logger.IsEnabled(LogLevel.Debug)) Logger.LogDebug("Connection recovery started.");
            };

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

        public event EventHandler<ShutdownEventArgs> ChannelShutdown
        {
            add
            {
                lock (_lock)
                {
                    ChannelShutdownInternal += value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ChannelShutdown += value;
                }
            }
            remove
            {
                lock (_lock)
                {
                    ChannelShutdownInternal -= value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ChannelShutdown -= value;
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

        public event EventHandler<ShutdownEventArgs> ConnectionShutdown
        {
            add
            {
                lock (_lock)
                {
                    ConnectionShutdownInternal += value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ConnectionShutdown += value;
                }
            }
            remove
            {
                lock (_lock)
                {
                    ConnectionShutdownInternal -= value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ConnectionShutdown -= value;
                }
            }
        }

        private event EventHandler<ChannelCreatedEventArgs> ChannelCreatedInternal;
        private event EventHandler<ShutdownEventArgs> ChannelShutdownInternal;
        private event EventHandler<IConnection> ConnectionCreatedInternal;
        private event EventHandler<IConnection> ConnectionClosedInternal;
        private event EventHandler<ShutdownEventArgs> ConnectionShutdownInternal;
        private event EventHandler RecoverySucceededInternal;

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

        public int CloseTimeout
        {
            get => _closeTimeout;
            set
            {
                _closeTimeout = value;

                if (_publisherConnectionFactory != null) _publisherConnectionFactory.CloseTimeout = value;
            }
        }

        public IConnectionFactory PublisherConnectionFactory => _publisherConnectionFactory;

        public bool HasPublisherConnectionFactory => _publisherConnectionFactory != null;

        public bool IsSimplePublisherConfirms => false;

        public bool IsPublisherConfirms => false;

        public bool IsPublisherReturns => false;

        #endregion

        #region Methods

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

        public void SetChannelShutDownHandlers(IEnumerable<EventHandler<ShutdownEventArgs>> handlers)
        {
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));

            ClearChannelShutDownHandlers();

            foreach (var handler in handlers) ChannelShutdown += handler;
        }

        public void ClearChannelShutDownHandlers()
        {
            lock (_lock)
            {
                ChannelShutdownInternal = null;

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

        public void SetConnectionShutDownHandlers(IEnumerable<EventHandler<ShutdownEventArgs>> handlers)
        {
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));

            ClearConnectionShutDownHandlers();

            foreach (var handler in handlers) ConnectionShutdown += handler;
        }

        public void ClearConnectionShutDownHandlers()
        {
            lock (_lock)
            {
                ConnectionShutdownInternal = null;

                if (_publisherConnectionFactory != null) _publisherConnectionFactory.ClearConnectionShutDownHandlers();
            }
        }

        public void SetRecoverySucceededHandler(EventHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            lock (_lock) RecoverySucceededInternal = handler;

            if (_publisherConnectionFactory != null) _publisherConnectionFactory.SetRecoverySucceededHandler(handler);
        }

        public void SetConnectionNameStrategy(ConnectionNameStrategy connectionNameStrategy)
        {
            _connectionNameStrategy = connectionNameStrategy;

            if (_publisherConnectionFactory != null)
                _publisherConnectionFactory.SetConnectionNameStrategy(connectionFactory =>
                    $"{connectionNameStrategy(connectionFactory)}{PublisherSuffix}");
        }

        public void SetShuffleAddresses(bool shuffleAddresses)
        {
            _shuffleAddresses = shuffleAddresses;
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

        protected IConnection CreateBareConnection()
        {
            try
            {
                var connectionName = _connectionNameStrategy(this);

                var rabbitConnection = Connect(connectionName);

                var connection = new SimpleConnection(rabbitConnection, _closeTimeout);

                if (rabbitConnection is AutorecoveringConnection autorecoveringConnection)
                {
                    autorecoveringConnection.RecoverySucceeded += (sender, args) =>
                    {
                        try
                        {
                            connection.Dispose();
                        }
                        catch (Exception e)
                        {
                            Logger.LogError(e, "Failed to close auto-recover connection");
                        }
                    };

                    autorecoveringConnection.RecoverySucceeded +=
                        (sender, e) => RecoverySucceededInternal?.Invoke(sender, e);
                }

                if (Logger.IsEnabled(LogLevel.Information))
                    Logger.LogInformation("Created new connection: {connectionName}/{connection}", connectionName,
                        connection);

                return connection;
            }
            catch (IOException e)
            {
                throw RabbitExceptionTranslator.ConvertRabbitAccessException(e);
            }
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

        private RabbitMQ.Client.IConnection Connect(string connectionName)
        {
            RabbitMQ.Client.IConnection rabbitConnection;

            if (_addresses != null)
            {
                var addressesToConnect = _addresses;

                if (_shuffleAddresses && _addresses.Count > 1)
                {
                    var list = new List<AmqpTcpEndpoint>(addressesToConnect);
                    list.Shuffle();
                    addressesToConnect = list;
                }

                if (Logger.IsEnabled(LogLevel.Information))
                    Logger.LogInformation("Attempting to connect to: {addressesToConnect}", addressesToConnect);

                var resolver = new EndpointResolver(addressesToConnect);

                rabbitConnection = RabbitConnectionFactory.CreateConnection(resolver, connectionName);
            }
            else
            {
                if (Logger.IsEnabled(LogLevel.Information))
                    Logger.LogInformation("Attempting to connect to: {host}:{port}", RabbitConnectionFactory.HostName,
                        RabbitConnectionFactory.Port);

                rabbitConnection = RabbitConnectionFactory.CreateConnection(connectionName);
            }

            return rabbitConnection;
        }

        #endregion

        private class EndpointResolver : IEndpointResolver
        {
            private readonly IEnumerable<AmqpTcpEndpoint> _endpoints;

            public EndpointResolver(IEnumerable<AmqpTcpEndpoint> endpoints)
            {
                _endpoints = endpoints;
            }

            public IEnumerable<AmqpTcpEndpoint> All() => _endpoints;
        }
    }
}
