using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Spring.Amqp.Rabbit.Support;
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
        private const string Unused = "unused";

        private static readonly ISet<string> AckMethods = new HashSet<string>
        {
            "basicAck",
            "basicNack",
            "basicReject"
        };

        private static readonly ISet<string> TxEnds = new HashSet<string>
        {
            "txCommit",
            "txRollback"
        };

        private static readonly ISet<string> TxStarts = new HashSet<string>
        {
            "basicPublish",
            "basicAck",
            "basicNack",
            "basicReject"
        };

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
        private readonly ConcurrentQueue<ChannelCachingConnectionProxy> _idleConnections
            = new ConcurrentQueue<ChannelCachingConnectionProxy>();
        private readonly CachingConnectionFactory _publisherConnectionFactory;
        private readonly object _connectionMonitor = new object();

        private FactoryCacheMode _cacheMode = FactoryCacheMode.Channel;
        private int _channelCheckoutTimeout;
        private int _channelCacheSize = DefaultChannelCacheSize;
        private ConditionalExceptionLogger _closeExceptionLogger;
        private int _connectionCacheSize = 1;
        private int _connectionHighWaterMark;
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

            _closeExceptionLogger = (logger, message, exception, args) =>
            {
                if (exception is OperationInterruptedException oie)
                {
                    if (RabbitUtils.IsPassiveDeclarationChannelClose(oie) && logger.IsEnabled(LogLevel.Debug))
                        logger.LogDebug(oie, message, args);
                    else if (RabbitUtils.IsExclusiveUseChannelClose(oie) && logger.IsEnabled(LogLevel.Information))
                        logger.LogInformation(oie, message, args);
                    else if (!RabbitUtils.IsNormalChannelClose(oie))
                        logger.LogError(oie, message, args);
                }
                else
                {
                    logger.LogError(exception, "Unexpected invocation of {type} with message: ", GetType());
                }
            };
        }

        public CachingConnectionFactory(Uri uri) : base(NewRabbitConnectionFactory())
        {
            _connection = new ChannelCachingConnectionProxy(this, null);

            Uri = uri;

            _publisherConnectionFactory = new CachingConnectionFactory(RabbitConnectionFactory, true);
            SetPublisherConnectionFactory(_publisherConnectionFactory);

            _closeExceptionLogger = (logger, message, exception, args) =>
            {
                if (exception is OperationInterruptedException oie)
                {
                    if (RabbitUtils.IsPassiveDeclarationChannelClose(oie) && logger.IsEnabled(LogLevel.Debug))
                        logger.LogDebug(oie, message, args);
                    else if (RabbitUtils.IsExclusiveUseChannelClose(oie) && logger.IsEnabled(LogLevel.Information))
                        logger.LogInformation(oie, message, args);
                    else if (!RabbitUtils.IsNormalChannelClose(oie))
                        logger.LogError(oie, message, args);
                }
                else
                {
                    logger.LogError(exception, "Unexpected invocation of {type} with message: {message}", GetType(),
                        message, args);
                }
            };
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

            _closeExceptionLogger = (logger, message, exception, args) =>
            {
                if (exception is OperationInterruptedException oie)
                {
                    if (RabbitUtils.IsPassiveDeclarationChannelClose(oie) && logger.IsEnabled(LogLevel.Debug))
                        logger.LogDebug(oie, message, args);
                    else if (RabbitUtils.IsExclusiveUseChannelClose(oie) && logger.IsEnabled(LogLevel.Information))
                        logger.LogInformation(oie, message, args);
                    else if (!RabbitUtils.IsNormalChannelClose(oie))
                        logger.LogError(oie, message, args);
                }
                else
                {
                    logger.LogError(exception, "Unexpected invocation of {type} with message: ", GetType());
                }
            };
        }

        #endregion

        #region Events

        public override event EventHandler<IConnection> ConnectionCreated
        {
            add
            {
                base.ConnectionCreated += value;

                if (_connection.TargetConnection != null) OnConnectionCreated(_connection);
            }
            remove => base.ConnectionCreated -= value;
        }

        #endregion

        #region Enums

        /// <summary>
        /// The type of publisher confirms to use.
        /// </summary>
        public enum ConfirmType
        {
            /// <summary>
            /// Publisher confirms are disabled (default).
            /// </summary>
            None,

            /// <summary>
            /// Use WaitForConfirmsOrDie within scoped operations.
            /// </summary>
            Simple,

            /// <summary>
            /// Use with CorrelationData to correlate confirmations with sent messsages.
            /// </summary>
            Correlated
        }

        public enum FactoryCacheMode
        {
            /// <summary>
            /// Cache channels - single connection.
            /// </summary>
            Channel,

            /// <summary>
            /// Cache connections and channels within each connection.
            /// </summary>
            Connection
        }

        #endregion

        #region Properties

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

        public FactoryCacheMode CacheMode
        {
            get => _cacheMode;
            set
            {
                if (_initialized)
                    throw new InvalidOperationException($"'{nameof(CacheMode)}' cannot be changed after initialization.");

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

        public override bool IsPublisherConfirms => _confirmType == ConfirmType.Correlated;

        public override bool IsPublisherReturns => _publisherReturns;

        public override bool IsSimplePublisherConfirms => _confirmType == ConfirmType.Simple;

        #endregion

        /// <summary>
        /// Set the connection limit when using cache mode Connection. When the limit is reached and there are no idle
        /// connections, the <see cref="SetChannelCheckoutTimeout(int)"/> is used to wait for a connection to become
        /// idle.
        /// </summary>
        /// <param name="connectionLimit">The limit.</param>
        public void SetConnectionLimit(int connectionLimit)
        {
            if (connectionLimit < 1)
                throw new ArgumentOutOfRangeException(nameof(connectionLimit), "Connection limit must be 1 or higher.");

            _connectionLimit = connectionLimit;

            if (_publisherConnectionFactory != null) _publisherConnectionFactory.SetConnectionLimit(connectionLimit);
        }

        /// <summary>
        /// Sets the channel checkout timeout.
        /// </summary>
        /// <param name="channelCheckoutTimeout">
        /// The timeout in milliseconds; default 0 (channel limiting not enabled).
        /// </param>
        public void SetChannelCheckoutTimeout(int channelCheckoutTimeout)
        {
            _channelCheckoutTimeout = channelCheckoutTimeout;

            if (_publisherConnectionFactory != null)
                _publisherConnectionFactory.SetChannelCheckoutTimeout(channelCheckoutTimeout);
        }

        /// <summary>
        /// Set the strategy for logging close exceptions; by default, if a channel is closed due to a failed passive
        /// queue declaration, it is logged at debug level.
        /// </summary>
        /// <param name="closeExceptionLogger">The <see cref="ConditionalExceptionLogger"/>.</param>
        public void SetCloseExceptionLogger(ConditionalExceptionLogger closeExceptionLogger)
        {
            _closeExceptionLogger = closeExceptionLogger ??
                throw new ArgumentNullException(nameof(closeExceptionLogger));

            if (_publisherConnectionFactory != null)
                _publisherConnectionFactory.SetCloseExceptionLogger(closeExceptionLogger);
        }

        public void SetPublisherChannelFactory()
        {

        }

        /// <summary>
        /// Use full (correlated) publisher confirms, with correlation data and a callback for each message.
        /// </summary>
        /// <param name="publisherReturns">True for full publisher returns.</param>
        [Obsolete("Deprecated in favor of SetPublisherConfirmType(ConfirmType).")]
        public void SetPublisherConfirms(bool publisherConfirms)
        {
            if (publisherConfirms && _confirmType == ConfirmType.Simple)
                throw new ArgumentOutOfRangeException(nameof(publisherConfirms),
                    "Cannot set both publisher confirms and simple publisher confirms.");

            if (publisherConfirms)
                SetPublisherConfirmType(ConfirmType.Correlated);
            else if (_confirmType == ConfirmType.Correlated)
                SetPublisherConfirmType(ConfirmType.None);
        }

        /// <summary>
        /// Use simple publisher confirms where the template simply waits for completion.
        /// </summary>
        /// <param name="simplePublisherConfirms">True for confirms.</param>
        [Obsolete("Deprecated in favor of SetPublisherConfirmType(ConfirmType).")]
        public void SetSimplePublisherConfirms(bool simplePublisherConfirms)
        {
            if (simplePublisherConfirms && _confirmType == ConfirmType.Correlated)
                throw new ArgumentOutOfRangeException(nameof(simplePublisherConfirms),
                    "Cannot set both publisher confirms and simple publisher confirms.");

            if (simplePublisherConfirms)
                SetPublisherConfirmType(ConfirmType.Simple);
            else if (_confirmType == ConfirmType.Simple)
                SetPublisherConfirmType(ConfirmType.None);
        }

        /// <summary>
        /// Set the confirm type to use; default None.
        /// </summary>
        /// <param name="confirmType">The confirm type.</param>
        public void SetPublisherConfirmType(ConfirmType confirmType)
        {
            _confirmType = confirmType;

            if (_publisherConnectionFactory != null) _publisherConnectionFactory.SetPublisherConfirmType(confirmType);
        }

        public void SetPublisherReturns(bool publisherReturns)
        {
            _publisherReturns = publisherReturns;

            if (_publisherConnectionFactory != null) _publisherConnectionFactory.SetPublisherReturns(publisherReturns);
        }

        public void AfterPropertiesSet()
        {
            _initialized = true;

            if (_cacheMode == FactoryCacheMode.Channel && _connectionCacheSize != 1)
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

        protected void CloseAndClear(ICollection<IChannelProxy> channels)
        {
            if (channels == null) throw new ArgumentNullException(nameof(channels));

            lock (channels)
            {
                CloseChannels(channels);
                channels.Clear();
            }
        }

        protected void CloseChannels(ICollection<IChannelProxy> channels)
        {
            if (channels == null) throw new ArgumentNullException(nameof(channels));

            foreach (var channel in channels)
            {
                try
                {
                    channel.Close();
                }
                catch (Exception e)
                {
                    Logger.LogTrace(e, "Could not close cached Rabbit Channel");
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        protected void Reset(ICollection<IChannelProxy> channels, ICollection<IChannelProxy> txChannels,
            IDictionary<IModel, IChannelProxy> channelsAwaitingAcks)
        {
            if (channels == null) throw new ArgumentNullException(nameof(channels));
            if (txChannels == null) throw new ArgumentNullException(nameof(txChannels));
            if (channelsAwaitingAcks == null) throw new ArgumentNullException(nameof(channelsAwaitingAcks));

            _active = false;
            CloseAndClear(channels);
            CloseAndClear(txChannels);
            CloseChannels(channelsAwaitingAcks.Values);
            channelsAwaitingAcks.Clear();
            _active = true;
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
            _closeExceptionLogger(Logger, "Channel shutdown", null, eventArgs);

            var protocolClassId = eventArgs.ClassId;
            if (protocolClassId == RabbitUtils.ChannelProtocolClassId20)
                OnChannelShutdown(eventArgs);
            else if (protocolClassId == RabbitUtils.ConnectionProtocolClassId10)
                OnConnectionShutdown(eventArgs);
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
                (FactoryCacheMode.Channel, true) => _cachedChannelsTransactional,
                (FactoryCacheMode.Channel, false) => _cachedChannelsNonTransactional,
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

        private IChannelProxy GetCachedChannelProxy(ChannelCachingConnectionProxy connection,
            LinkedList<IChannelProxy> channelList, bool transactional)
        {
            var targetChannel = CreateBareChannel(connection, transactional);

            if (Logger.IsEnabled(LogLevel.Debug))
                Logger.LogDebug("Creating cached Rabbit Channel from {targetChannel}.", targetChannel);

            OnChannelCreated(new ChannelCreatedEventArgs
            {
                Channel = targetChannel,
                Transactional = transactional
            });

            if (_confirmType == ConfirmType.Correlated || _publisherReturns)
        }

        private IModel CreateBareChannel(ChannelCachingConnectionProxy connection, bool transactional)
        {
            if (_cacheMode == FactoryCacheMode.Channel)
            {
                if (_connection.IsOpen()) return DoCreateBareChannel(_connection, transactional);

                lock (_connectionMonitor)
                {
                    if (!_connection.IsOpen()) _connection.NotifyCloseIfNecessary();

                    if (!_connection.IsOpen())
                    {
                        _connection.TargetConnection = null;
                        CreateConnection();
                    }
                }

                return DoCreateBareChannel(_connection, transactional);
            }
            else if (_cacheMode == FactoryCacheMode.Channel)
            {
                if (connection.IsOpen()) return DoCreateBareChannel(connection, transactional);

                lock (_connectionMonitor)
                {
                    if (_allocatedConnectionNonTransactionalChannels.TryGetValue(connection, out var channel))
                        channel.Clear();
                    if (_allocatedConnectionTransactionalChannels.TryGetValue(connection, out channel))
                        channel.Clear();

                    connection.NotifyCloseIfNecessary();

                    RefreshProxyConnection(connection);
                }

                return DoCreateBareChannel(connection, transactional);
            }

            return null;
        }

        private void RefreshProxyConnection(ChannelCachingConnectionProxy connection)
        {
            connection.Close();
            connection.NotifyCloseIfNecessary();
            
            connection.TargetConnection = CreateBareConnection();
            
            connection.CloseNotified = false;

            if (Logger.IsEnabled(LogLevel.Debug))
                Logger.LogDebug("Refreshed existing connection '{connection}'", connection);
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
                catch (IOException e)
                {
                    Logger.LogError(e, "Could not configure the channel to receive publisher confirms.");
                }
            }

            //if ((ConfirmType.CORRELATED.equals(this.confirmType) || this.publisherReturns)
            //        && !(channel instanceof PublisherCallbackChannelImpl)) {
            //    channel = this.publisherChannelFactory.createChannel(channel, getChannelsExecutor());
            //}

            if (channel != null) channel.ModelShutdown += ShutdownCompleted;

            return channel;
        }

        public override IConnection CreateConnection()
        {
            throw new NotImplementedException();
        }

        private class ChannelCachingConnectionProxy : IConnectionProxy
        {
            private readonly ConcurrentDictionary<IModel, IChannelProxy> _channelsAwaitingAcks
                = new ConcurrentDictionary<IModel, IChannelProxy>();
            private readonly CachingConnectionFactory _factory;

            private int _closeNotified;
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

            #region Properties

            public IConnection TargetConnection
            {
                get => _target;
                internal set => _target = value;
            }

            public int LocalPort => _target?.LocalPort ?? 0;

            public RabbitMQ.Client.IConnection DelegateConnection => _target.DelegateConnection;

            internal bool CloseNotified
            {
                get => _closeNotified > 0;
                set => Interlocked.Exchange(ref _closeNotified, value ? 1 : 0);
            }

            #endregion

            public override string ToString()
            {
                return $"Proxy@{this.GetIdentityHexString()} " +
                    $"{(_factory._cacheMode == FactoryCacheMode.Channel ? "Shared" : "Dedicated")} " +
                    $"Rabbit Connection: {_target}";
            }

            public IModel CreateChannel(bool transactional)
            {
                return _factory.GetChannel(this, transactional);
            }

            public void Close()
            {
                if (_factory._cacheMode == FactoryCacheMode.Channel)
                {
                    _factory.Reset(_factory._cachedChannelsNonTransactional, _factory._cachedChannelsTransactional,
                        _channelsAwaitingAcks);
                }
                else if (_factory._allocatedConnectionNonTransactionalChannels.TryGetValue(this, out var channels) &&
                         _factory._allocatedConnectionTransactionalChannels.TryGetValue(this, out var txChannels))
                {
                    _factory.Reset(channels, txChannels, _channelsAwaitingAcks);
                }

                if (_target != null)
                {
                    RabbitUtils.CloseConnection(_target);
                    NotifyCloseIfNecessary();
                }

                _target = null;
            }

            public void Dispose()
            {
                Dispose(true);
            }

            public bool IsOpen()
            {
                return _target?.IsOpen() ?? false;
            }

            internal IModel CreateBareChannel(bool transactional)
            {
                if (_target == null)
                    throw new InvalidOperationException("Can't create channel - no target connection.");

                return _target.CreateChannel(transactional);
            }

            internal void NotifyCloseIfNecessary()
            {
                if (Interlocked.Exchange(ref _closeNotified, 1) == 0) _factory.OnConnectionClosed(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!_disposedValue)
                {
                    if (disposing) DisposeInternal();

                    _disposedValue = true;
                }
            }

            private int CountOpenIdleConnections()
            {
                return _factory._idleConnections.Count(connection => connection.IsOpen());
            }

            private void DisposeInternal()
            {
                if (_factory._cacheMode != FactoryCacheMode.Connection) return;

                lock (_factory._connectionMonitor)
                {
                    if (_factory._idleConnections.Contains(this)) return;

                    if (!IsOpen() || CountOpenIdleConnections() >= _factory.ConnectionCacheSize)
                    {
                        if (_factory.Logger.IsEnabled(LogLevel.Debug))
                            _factory.Logger.LogDebug("Completely closing connection '{connection}'.", this);

                        Close();
                    }

                    if (_factory.Logger.IsEnabled(LogLevel.Debug))
                        _factory.Logger.LogDebug("Returning connection '{connection}' to cache.", this);

                    _factory._idleConnections.Enqueue(this);

                    if (_factory._connectionHighWaterMark < _factory._idleConnections.Count)
                        Interlocked.Exchange(ref _factory._connectionHighWaterMark, _factory._idleConnections.Count);
                }
            }
        }
    }
}
