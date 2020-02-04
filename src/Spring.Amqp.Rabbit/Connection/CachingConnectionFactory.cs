using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Spring.Amqp.Rabbit.Connection
{
    public class CachingConnectionFactory : AbstractConnectionFactory
    {
        #region Fields

        private const int ChannelExecShutdownTimeout = 30;
        private const int DefaultChannelCacheSize = 25;
        private const string DefaultDeferredPoolPrefix = "spring-rabbit-deferred-pool-";

        private static int _threadPoolId;

        private readonly ChannelCachingConnectionProxy _connection;
        private readonly ISet<ChannelCachingConnectionProxy> _allocatedConnections
            = new HashSet<ChannelCachingConnectionProxy>();
        private readonly IDictionary<ChannelCachingConnectionProxy, LinkedList<IChannelProxy>> _allocatedConnectionNonTransactionalChannels
            = new Dictionary<ChannelCachingConnectionProxy, LinkedList<IChannelProxy>>();
        private readonly IDictionary<ChannelCachingConnectionProxy, LinkedList<IChannelProxy>> _allocatedConnectionTransactionalChannels
            = new Dictionary<ChannelCachingConnectionProxy, LinkedList<IChannelProxy>>();
        private readonly LinkedList<IChannelProxy> _cachedChannelsNonTransactional = new LinkedList<IChannelProxy>();
        private readonly LinkedList<IChannelProxy> _cachedChannelsTransactional = new LinkedList<IChannelProxy>();
        private readonly IDictionary<IConnection, SemaphoreSlim> _checkoutPermits
            = new Dictionary<IConnection, SemaphoreSlim>();
        private readonly ConcurrentDictionary<string, int> _channelHighWaterMarks
            = new ConcurrentDictionary<string, int>();
        private readonly CachingConnectionFactory _publisherConnectionFactory;
        private readonly object _connectionMonitor = new object();

        private int _connectionHighWaterMark;
        private int _channelCheckoutTimeout;
        private CacheMode _cacheMode = CacheMode.Channel;
        private int _channelCacheSize = DefaultChannelCacheSize;
        private int _connectionCacheSize = 1;
        private int _connectionLimit = int.MaxValue;
        private ConfirmType _confirmType = ConfirmType.None;
        private bool _publisherReturns;
        private volatile bool _active = true;
        private volatile bool _initialized;
        private volatile bool _stopped;

        #endregion

        #region Constructors

        public CachingConnectionFactory() : this(null as string)
        {
        }

        public CachingConnectionFactory(string hostname) : this(hostname, AmqpTcpEndpoint.UseDefaultPort)
        {
        }

        public CachingConnectionFactory(int port) : this(null, port)
        {
        }
        
        public CachingConnectionFactory(string hostNameArg, int port) : base(NewRabbitConnectionFactory())
        {
            _connection = new ChannelCachingConnectionProxy(this, null);

            Host = string.IsNullOrWhiteSpace(hostNameArg) ? GetDefaultHostName() : hostNameArg;
            Port = port;

            _publisherConnectionFactory = new CachingConnectionFactory(RabbitConnectionFactory, true);
            SetPublisherConnectionFactory(_publisherConnectionFactory);
        }

        public CachingConnectionFactory(Uri uri) : base(NewRabbitConnectionFactory())
        {
            _connection = new ChannelCachingConnectionProxy(this, null);

            SetUri(uri);

            _publisherConnectionFactory = new CachingConnectionFactory(RabbitConnectionFactory, true);
            SetPublisherConnectionFactory(_publisherConnectionFactory);
        }

        public CachingConnectionFactory(ConnectionFactory rabbitConnectionFactory) : this(rabbitConnectionFactory, false)
        {
        }

        private CachingConnectionFactory(ConnectionFactory rabbitConnectionFactory, bool isPublisherFactory)
            : base(rabbitConnectionFactory)
        {
            if (rabbitConnectionFactory == null) throw new ArgumentNullException(nameof(rabbitConnectionFactory));

            _connection = new ChannelCachingConnectionProxy(this, null);

            if (!isPublisherFactory)
            {
                if (rabbitConnectionFactory.AutomaticRecoveryEnabled)
                {
                    rabbitConnectionFactory.AutomaticRecoveryEnabled = false;
                    Logger.LogWarning("***\nAutomatic Recovery was Enabled in the provided connection factory;\n"
                        + "while Spring AMQP is generally compatible with this feature, there\n"
                        + "are some corner cases where problems arise. Spring AMQP\n"
                        + "prefers to use its own recovery mechanisms; when this option is true, you may receive\n"
                        + "'AutoRecoverConnectionNotCurrentlyOpenException's until the connection is recovered.\n"
                        + "It has therefore been disabled; if you really wish to enable it, use\n"
                        + "'RabbitConnectionFactory.AutomaticRecoveryEnabled = true',\n"
                        + "but this is discouraged.");
                }

                _publisherConnectionFactory = new CachingConnectionFactory(RabbitConnectionFactory, true);
                SetPublisherConnectionFactory(_publisherConnectionFactory);
            }
            else
            {
                _publisherConnectionFactory = null;
            }
        }

        #endregion

        public int ChannelCacheSize
        {
            get => _channelCacheSize;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(ChannelCacheSize),
                        "Channel cache size must be 1 or higher.");
                
                _channelCacheSize = value;

                if (_publisherConnectionFactory != null) _publisherConnectionFactory.ChannelCacheSize = value;
            }
        }

        public CacheMode CacheMode
        {
            get => _cacheMode;
            set
            {
                if (_initialized)
                    throw new InvalidOperationException("'cacheMode' cannot be changed after initialization.");

                _cacheMode = value;

                if (_publisherConnectionFactory != null) _publisherConnectionFactory.CacheMode = value;
            }
        }

        public int ConnectionCacheSize
        {
            get => _connectionCacheSize;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(ConnectionCacheSize),
                        "Connection cache size must be 1 or higher.");

                _connectionCacheSize = value;

                if (_publisherConnectionFactory != null) _publisherConnectionFactory.ConnectionCacheSize = value;
            }
        }

        public bool PublisherConfirms => _confirmType == ConfirmType.Correlated;

        public bool PublisherReturns
        {
            get => _publisherReturns;
            set
            {
                _publisherReturns = value;

                if (_publisherConnectionFactory != null) _publisherConnectionFactory.PublisherReturns = value;
            }
        }

        public void SetConnectionLimit(int connectionLimit)
        {
            if (connectionLimit < 1)
                throw new ArgumentOutOfRangeException(nameof(connectionLimit), "Connection limit must be 1 or higher.");

            _connectionLimit = connectionLimit;

            if (_publisherConnectionFactory != null) _publisherConnectionFactory.SetConnectionLimit(connectionLimit);
        }

        public void SetChannelCheckoutTimeout(int channelCheckoutTimeout)
        {
            _channelCheckoutTimeout = channelCheckoutTimeout;

            if (_publisherConnectionFactory != null)
                _publisherConnectionFactory.SetChannelCheckoutTimeout(channelCheckoutTimeout);
        }

        public void AfterPropertiesSet()
        {
            _initialized = true;

            if (_cacheMode == CacheMode.Channel && _connectionCacheSize != 1)
                throw new InvalidOperationException("When the cache mode is 'Channel', the connection cache size cannot be configured.");

            InitCacheWaterMarks();

            if (_publisherConnectionFactory != null) _publisherConnectionFactory.AfterPropertiesSet();
        }

        public void ResetConnection()
        {
            lock (_connectionMonitor)
            {
                if (_connection.TargetConnection != null) _connection.Dispose();

                foreach (var connection in _allocatedConnections) connection.Dispose();

                foreach (var waterMark in _channelHighWaterMarks)
                    _channelHighWaterMarks.TryUpdate(waterMark.Key, 0, waterMark.Value);

                Interlocked.Exchange(ref _connectionHighWaterMark, 0);
            }

            if (_publisherConnectionFactory != null) _publisherConnectionFactory.ResetConnection();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        private static ConnectionFactory NewRabbitConnectionFactory()
        {
            return new ConnectionFactory { AutomaticRecoveryEnabled = false };
        }

        private void InitCacheWaterMarks()
        {
            _channelHighWaterMarks.TryAdd(_cachedChannelsNonTransactional.GetIdentityHexString(), 0);
            _channelHighWaterMarks.TryAdd(_cachedChannelsTransactional.GetIdentityHexString(), 0);
        }

        private void ShutdownCompleted(object sender, ShutdownEventArgs eventArgs)
        {
            //this.closeExceptionLogger.log(logger, "Channel shutdown", eventArgs);
            var protocolClassId = eventArgs.ClassId;
            if (protocolClassId == RabbitUtils.ChannelProtocolClassId20)
            {
                //getChannelListener().onShutDown(eventArgs);
            }
            else if (protocolClassId == RabbitUtils.ConnectionProtocolClassId10)
            {
                //getConnectionListener().onShutDown(eventArgs);
            }
        }

        private IModel GetChannel(ChannelCachingConnectionProxy connection, bool transactional)
        {
            var permits = _channelCheckoutTimeout > 0 ? ObtainPermits(connection) : null;

            var channelList = DetermineChannelList(connection, transactional);

            IChannelProxy channel = null;

            if (connection.IsOpen())
            {
                channel = FindOpenChannel(channelList, channel);

                if (channel != null && Logger.IsEnabled(LogLevel.Trace))
                    Logger.LogTrace("Found cached Rabbit Channel: {channel}", channel);
            }

            if (channel == null)
            {
                try
                {
                    //channel = GetCachedChannelProxy(connection, channelList, transactional);
                }
                catch (Exception)
                {
                    if (permits != null)
                    {
                        permits.Release();

                        if (Logger.IsEnabled(LogLevel.Debug))
                            Logger.LogDebug("Could not get channel; released permit for {connection}, remaining: {availablePermits}",
                                connection, permits.CurrentCount);

                        throw;
                    }
                }
            }

            return channel;
        }

        private SemaphoreSlim ObtainPermits(ChannelCachingConnectionProxy connection)
        {
            if (_checkoutPermits.TryGetValue(connection, out var permits))
            {
                try
                {
                    if (!permits.Wait(_channelCheckoutTimeout))
                        throw new AmqpTimeoutException("No available channels.");

                    if (Logger.IsEnabled(LogLevel.Debug))
                        Logger.LogDebug("Acquired permit for {connection}, remaining: {availablePermits}", connection,
                            permits.CurrentCount);
                }
                catch (ThreadInterruptedException ex)
                {
                    Thread.CurrentThread.Interrupt();

                    throw new AmqpTimeoutException("Interrupted while acquiring a channel.", ex);
                }
            }
            else
            {
                throw new InvalidOperationException($"No permits map entry for {connection}.");
            }

            return permits;
        }

        private IChannelProxy FindOpenChannel(LinkedList<IChannelProxy> channelList, IChannelProxy channelArg)
        {
            var channel = channelArg;

            lock (channelList)
            {
                while (channelList.Any())
                {
                    channel = channelList.First.Value;
                    channelList.RemoveFirst();

                    if (Logger.IsEnabled(LogLevel.Trace)) Logger.LogTrace("{channel} retrieved from cache.", channel);

                    if (channel.IsOpen)
                    {
                        break;
                    }
                    else
                    {
                        CleanUpClosedChannel(channel);
                        channel = null;
                    }
                }
            }

            return channel;
        }

        private void CleanUpClosedChannel(IChannelProxy channel)
        {
            try
            {
                var target = channel.TargetChannel;

                if (target != null) target.Close();
            }
            catch (AlreadyClosedException ex)
            {
                if (Logger.IsEnabled(LogLevel.Trace)) Logger.LogTrace(ex, "{channel} is already closed.", channel);
            }
            catch (IOException ex)
            {
                if (Logger.IsEnabled(LogLevel.Debug)) Logger.LogDebug(ex, "Unexpected Exception closing channel.");
            }
            catch (TimeoutException ex)
            {
                if (Logger.IsEnabled(LogLevel.Warning)) Logger.LogWarning(ex, "TimeoutException closing channel.");
            }
        }

        private LinkedList<IChannelProxy> DetermineChannelList(ChannelCachingConnectionProxy connection,
            bool transactional)
        {
            var channelList = (_cacheMode, transactional) switch
            {
                (CacheMode.Channel, true) => _cachedChannelsTransactional,
                (CacheMode.Channel, false) => _cachedChannelsNonTransactional,
                (_, true) => _allocatedConnectionTransactionalChannels.TryGetValue(connection, out var result)
                             ? result
                             : null,
                (_, false) => _allocatedConnectionNonTransactionalChannels.TryGetValue(connection, out var result)
                              ? result
                              : null
            };

            if (channelList == null)
                throw new InvalidOperationException($"No channel list for connection {connection}.");

            return channelList;
        }

        //private IChannelProxy GetCachedChannelProxy(ChannelCachingConnectionProxy connection,
        //    LinkedList<IChannelProxy> channelList, bool transactional)
        //{
        //    var targetChannel = CreateBareChannel(connection, transactional);

        //    if (Logger.IsEnabled(LogLevel.Debug))
        //        Logger.LogDebug("Creating cached Rabbit Channel from {targetChannel}.", targetChannel);
        //}

        private IModel CreateBareChannel(ChannelCachingConnectionProxy connection, bool transactional)
        {
            if (_cacheMode == CacheMode.Channel)
            {
                if (!_connection.IsOpen())
                {
                    lock (_connectionMonitor)
                    {
                        if (!_connection.IsOpen())
                        {
                            //_connection.notifyCloseIfNecessary();
                        }

                        if (!_connection.IsOpen())
                        {
                            _connection.TargetConnection = null;
                            //CreateConnection();
                        }
                    }
                }

                return DoCreateBareChannel(_connection, transactional);
            }
            else if (_cacheMode == CacheMode.Channel)
            {
                if (!connection.IsOpen())
                {
                    lock (_connectionMonitor)
                    {
                        if (_allocatedConnectionNonTransactionalChannels.TryGetValue(connection, out var channel))
                            channel.Clear();
                        if (_allocatedConnectionTransactionalChannels.TryGetValue(connection, out channel))
                            channel.Clear();

                        //connection.notifyCloseIfNecessary();

                        //refreshProxyConnection(connection);
                    }
                }

                return DoCreateBareChannel(connection, transactional);
            }

            return null;
        }

        private IModel DoCreateBareChannel(ChannelCachingConnectionProxy connection, bool transactional)
        {
            var channel = connection.CreateBareChannel(transactional);

            if (_confirmType != ConfirmType.None)
            {
                try
                {
                    channel.ConfirmSelect();
                }
                catch (IOException ex)
                {
                    Logger.LogError(ex, "Could not configure the channel to receive publisher confirms.");
                }
            }

            //if ((ConfirmType.CORRELATED.equals(this.confirmType) || this.publisherReturns)
            //        && !(channel instanceof PublisherCallbackChannelImpl)) {
            //    channel = this.publisherChannelFactory.createChannel(channel, getChannelsExecutor());
            //}

            if (channel != null) channel.ModelShutdown += ShutdownCompleted;

            return channel;
        }

        private class ChannelCachingConnectionProxy : IConnectionProxy
        {
            private readonly ConcurrentDictionary<IModel, IChannelProxy> _channelsAwaitingAcks
                = new ConcurrentDictionary<IModel, IChannelProxy>();
            private readonly CachingConnectionFactory _factory;

            private bool _closeNotified;
            private bool _disposedValue;
            private volatile IConnection _target;

            internal ChannelCachingConnectionProxy(CachingConnectionFactory factory, IConnection target)
            {
                _factory = factory;
                _target = target;
            }

            #region Events

            public event EventHandler<ConnectionBlockedEventArgs> ConnectionBlocked
            {
                add
                {
                    if (_target == null)
                        throw new ArgumentNullException(nameof(ConnectionBlocked),
                            "Can't add blocked event handler - no target connection.");

                    _target.ConnectionBlocked += value;
                }
                remove
                {
                    if (_target == null)
                        throw new ArgumentNullException(nameof(ConnectionBlocked),
                            "Can't remove blocked event handler - no target connection.");

                    _target.ConnectionBlocked -= value;
                }
            }

            public event EventHandler<EventArgs> ConnectionUnblocked
            {
                add
                {
                    if (_target == null)
                        throw new ArgumentNullException(nameof(ConnectionUnblocked),
                            "Can't add unblocked event handler - no target connection.");

                    _target.ConnectionUnblocked += value;
                }
                remove
                {
                    if (_target == null)
                        throw new ArgumentNullException(nameof(ConnectionUnblocked),
                            "Can't remove unblocked event handler - no target connection.");

                    _target.ConnectionUnblocked -= value;
                }
            }

            #endregion

            public IConnection TargetConnection
            {
                get => _target;
                internal set => _target = value;
            }

            public int LocalPort => _target?.LocalPort ?? 0;

            public RabbitMQ.Client.IConnection DelegateConnection => _target.DelegateConnection;

            public override string ToString()
            {
                return $"Proxy@{this.GetIdentityHexString()} " +
                    $"{(_factory._cacheMode == CacheMode.Channel ? "Shared" : "Dedicated")} " +
                    $"Rabbit Connection: {_target}";
            }

            public IModel CreateChannel(bool transactional)
            {
                return _factory.GetChannel(this, transactional);
            }

            public bool IsOpen()
            {
                return _target?.IsOpen() ?? false;
            }

            public void Dispose()
            {
                Dispose(true);
            }

            internal IModel CreateBareChannel(bool transactional)
            {
                if (_target == null)
                    throw new InvalidOperationException("Can't create channel - no target connection.");

                return _target.CreateChannel(transactional);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!_disposedValue)
                {
                    if (disposing)
                    {
                        // TODO: dispose managed state (managed objects).
                    }

                    _disposedValue = true;
                }
            }
        }
    }
}
