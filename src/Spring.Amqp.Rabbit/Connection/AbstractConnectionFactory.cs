using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
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
        public static readonly TimeSpan DefaultCloseTimeout = TimeSpan.FromMilliseconds(30000d);

        private const string BadUri = "Uri was passed an invalid URI; it is ignored";
        private const string PublisherSuffix = ".publish";

        private readonly object _lock = new object();

        private IList<AmqpTcpEndpoint> _addresses;
        private TimeSpan _closeTimeout = DefaultCloseTimeout;
        private ConnectionNameStrategy _connectionNameStrategy;
        private int _defaultConnectionNameStrategyCounter;
        private bool _disposed;
        private AbstractConnectionFactory _publisherConnectionFactory;
        private bool _shuffleAddresses;
        private EventHandler<ChannelCreatedEventArgs> _channelCreatedInternal;
        private EventHandler<ShutdownEventArgs> _channelShutdownInternal;
        private EventHandler<IConnection> _connectionCreatedInternal;
        private EventHandler<IConnection> _connectionClosedInternal;
        private EventHandler<ShutdownEventArgs> _connectionShutdownInternal;
        private EventHandler _recoverySucceededInternal;

        public AbstractConnectionFactory(ConnectionFactory rabbitConnectionFactory)
        {
            _connectionNameStrategy =
                connectionFactory =>
                    $"SpringAMQP#{this.GetIdentityHexString()}:{Interlocked.Increment(ref _defaultConnectionNameStrategyCounter) - 1}";

            _recoverySucceededInternal = (sender, e) =>
            {
                if (Logger.IsEnabled(LogLevel.Debug)) Logger.LogDebug("Connection recovery succeeded.");
            };

            RabbitConnectionFactory = rabbitConnectionFactory ??
                throw new ArgumentNullException(nameof(rabbitConnectionFactory),
                    "Target ConnectionFactory must not be null");
        }

        #region Events

        public virtual event EventHandler<ChannelCreatedEventArgs> ChannelCreated
        {
            add
            {
                lock (_lock)
                {
                    _channelCreatedInternal += value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ChannelCreated += value;
                }
            }
            remove
            {
                lock (_lock)
                {
                    _channelCreatedInternal -= value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ChannelCreated -= value;
                }
            }
        }

        public virtual event EventHandler<ShutdownEventArgs> ChannelShutdown
        {
            add
            {
                lock (_lock)
                {
                    _channelShutdownInternal += value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ChannelShutdown += value;
                }
            }
            remove
            {
                lock (_lock)
                {
                    _channelShutdownInternal -= value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ChannelShutdown -= value;
                }
            }
        }

        public virtual event EventHandler<IConnection> ConnectionCreated
        {
            add
            {
                lock (_lock)
                {
                    _connectionCreatedInternal += value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ConnectionCreated += value;
                }
            }
            remove
            {
                lock (_lock)
                {
                    _connectionCreatedInternal -= value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ConnectionCreated -= value;
                }
            }
        }

        public virtual event EventHandler<IConnection> ConnectionClosed
        {
            add
            {
                lock (_lock)
                {
                    _connectionClosedInternal += value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ConnectionClosed += value;
                }
            }
            remove
            {
                lock (_lock)
                {
                    _connectionClosedInternal -= value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ConnectionClosed -= value;
                }
            }
        }

        public virtual event EventHandler<ShutdownEventArgs> ConnectionShutdown
        {
            add
            {
                lock (_lock)
                {
                    _connectionShutdownInternal += value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ConnectionShutdown += value;
                }
            }
            remove
            {
                lock (_lock)
                {
                    _connectionShutdownInternal -= value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.ConnectionShutdown -= value;
                }
            }
        }

        public virtual event EventHandler RecoverySucceeded
        {
            add
            {
                lock (_lock)
                {
                    _recoverySucceededInternal += value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.RecoverySucceeded += value;
                }
            }
            remove
            {
                lock (_lock)
                {
                    _recoverySucceededInternal -= value;

                    if (_publisherConnectionFactory != null) _publisherConnectionFactory.RecoverySucceeded -= value;
                }
            }
        }

        #endregion

        #region Properties

        public ConnectionFactory RabbitConnectionFactory { get; }

        protected ILogger Logger { get; } = LogFactory.GetLogger<AbstractConnectionFactory>();

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

        public TimeSpan RequestedHeartbeat
        {
            get => RabbitConnectionFactory.RequestedHeartbeat;
            set => RabbitConnectionFactory.RequestedHeartbeat = value;
        }

        public TimeSpan RequestedConnectionTimeout
        {
            get => RabbitConnectionFactory.RequestedConnectionTimeout;
            set => RabbitConnectionFactory.RequestedConnectionTimeout = value;
        }

        public TimeSpan CloseTimeout
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

        public virtual bool IsSimplePublisherConfirms => false;

        public virtual bool IsPublisherConfirms => false;

        public virtual bool IsPublisherReturns => false;

        #endregion

        #region Methods

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

        public abstract IConnection CreateConnection();

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

                if (rabbitConnection is IAutorecoveringConnection autorecoveringConnection)
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

                    autorecoveringConnection.RecoverySucceeded += (sender, e) =>
                    {
                        var handler = _recoverySucceededInternal;

                        handler?.Invoke(sender, e);
                    };
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

        protected virtual void OnChannelCreated(ChannelCreatedEventArgs e)
        {
            var handler = _channelCreatedInternal;

            handler?.Invoke(this, e);
        }

        protected virtual void OnChannelShutdown(ShutdownEventArgs e)
        {
            var handler = _channelShutdownInternal;

            handler?.Invoke(this, e);
        }

        protected virtual void OnConnectionCreated(IConnection connection)
        {
            var handler = _connectionCreatedInternal;

            handler?.Invoke(this, connection);
        }

        protected virtual void OnConnectionClosed(IConnection connection)
        {
            var handler = _connectionClosedInternal;

            handler?.Invoke(this, connection);
        }

        protected virtual void OnConnectionShutdown(ShutdownEventArgs e)
        {
            var handler = _connectionShutdownInternal;

            handler?.Invoke(this, e);
        }

        protected virtual void OnRecoverySucceeded()
        {
            var handler = _recoverySucceededInternal;

            handler?.Invoke(this, EventArgs.Empty);
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
