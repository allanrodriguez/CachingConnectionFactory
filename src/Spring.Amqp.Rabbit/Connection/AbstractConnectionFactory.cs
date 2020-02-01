using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System;
using System.Net;
using System.Net.Sockets;

namespace Spring.Amqp.Rabbit.Connection
{
    public abstract class AbstractConnectionFactory : IDisposable
    {
        private readonly ConnectionFactory _rabbitConnectionFactory;
        
        private AbstractConnectionFactory _publisherConnectionFactory;
        private bool _disposedValue;

        public AbstractConnectionFactory(ConnectionFactory rabbitConnectionFactory)
        {
            _rabbitConnectionFactory = rabbitConnectionFactory ??
                throw new ArgumentNullException(nameof(rabbitConnectionFactory),
                    "Target ConnectionFactory must not be null");

            using (var loggerFactory = new LoggerFactory()) Logger = loggerFactory.CreateLogger(GetType());
        }

        public string Host
        {
            get => _rabbitConnectionFactory.HostName;
            set => _rabbitConnectionFactory.HostName = value;
        }

        public int Port
        {
            get => _rabbitConnectionFactory.Port;
            set => _rabbitConnectionFactory.Port = value;
        }

        public ConnectionFactory RabbitConnectionFactory => _rabbitConnectionFactory;

        protected ILogger Logger { get; }

        public void SetUri(Uri uri)
        {
            _rabbitConnectionFactory.Uri = uri;
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
            if (_disposedValue) return;

            if (disposing && _publisherConnectionFactory != null) _publisherConnectionFactory.Dispose();

            _disposedValue = true;
        }
    }
}
