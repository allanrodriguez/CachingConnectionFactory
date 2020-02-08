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

        public event EventHandler<IConnection> ConnectionCreated;
        public event EventHandler<IConnection> ConnectionClosed;
        public event EventHandler<OperationInterruptedException> ConnectionShutDown;

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

        public string Username
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

        public IConnectionFactory PublisherConnectionFactory => _publisherConnectionFactory;

        public bool IsSimplePublisherConfirms => throw new NotImplementedException();

        public bool IsPublisherConfirms => throw new NotImplementedException();

        public bool IsPublisherReturns => throw new NotImplementedException();

        public IConnection CreateConnection()
        {
            throw new NotImplementedException();
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

            Logger.LogInformation("setAddresses() called with an empty value, will be using the host+port " +
                    "properties for connections");
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
                Logger.LogDebug($"Using hostname [{temp}] for hostname.");
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
