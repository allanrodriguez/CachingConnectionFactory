using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Spring.Amqp.Rabbit.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
        private readonly IDictionary<IConnection, Semaphore> _checkoutPermits
            = new Dictionary<IConnection, Semaphore>();
        private readonly IDictionary<string, int> _channelHighWatermarks = new Dictionary<string, int>();
        private readonly int _connectionHighWatermark;
        private readonly CachingConnectionFactory _publisherConnectionFactory;
        private readonly object _connectionMonitor = new object();

        private long _channelCheckoutTimeout = 0L;
        private CacheMode _cacheMode = CacheMode.Channel;
        private int _channelCacheSize = DefaultChannelCacheSize;
        private int _connectionCacheSize = 1;
        private int _connectionLimit = int.MaxValue;
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
            Host = string.IsNullOrWhiteSpace(hostNameArg) ? GetDefaultHostName() : hostNameArg;
            Port = port;

            _publisherConnectionFactory = new CachingConnectionFactory(RabbitConnectionFactory, true);
            SetPublisherConnectionFactory(_publisherConnectionFactory);
        }

        public CachingConnectionFactory(Uri uri) : base(NewRabbitConnectionFactory())
        {
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

            if (!isPublisherFactory)
            {
                if (rabbitConnectionFactory.AutomaticRecoveryEnabled)
                {
                    rabbitConnectionFactory.AutomaticRecoveryEnabled = false;
                    _logger.LogWarning("***\nAutomatic Recovery was Enabled in the provided connection factory;\n"
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
                        "Channel cache size must be 1 or higher");
                
                _channelCacheSize = value;

                if (_publisherConnectionFactory != null) _publisherConnectionFactory.ChannelCacheSize = value;
            }
        }

        private static ConnectionFactory NewRabbitConnectionFactory()
        {
            return new ConnectionFactory { AutomaticRecoveryEnabled = false };
        }

        private IModel GetChannel(ChannelCachingConnectionProxy connection, bool transactional)
        {
            throw new NotImplementedException();
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

            public event EventHandler<ConnectionBlockedEventArgs> ConnectionBlocked;
            public event EventHandler ConnectionUnblocked;

            public IConnection TargetConnection => _target;

            public int LocalPort => _target?.LocalPort ?? 0;

            public RabbitMQ.Client.IConnection DelegateConnection => _target.DelegateConnection;

            public override string ToString()
            {
                return $"Proxy@{GetHashCode().ToString("x", CultureInfo.InvariantCulture)} " +
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
