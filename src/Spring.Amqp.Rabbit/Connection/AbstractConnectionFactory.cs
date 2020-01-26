using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System;
using System.Net;
using System.Net.Sockets;

namespace Spring.Amqp.Rabbit.Connection
{
    public abstract class AbstractConnectionFactory
    {
        protected readonly ILogger _logger;
        private readonly ConnectionFactory _rabbitConnectionFactory;
        private AbstractConnectionFactory _publisherConnectionFactory;

        public AbstractConnectionFactory(ConnectionFactory rabbitConnectionFactory)
        {
            _rabbitConnectionFactory = rabbitConnectionFactory ??
                throw new ArgumentNullException(nameof(rabbitConnectionFactory),
                    "Target ConnectionFactory must not be null");

            using (var loggerFactory = new LoggerFactory()) _logger = loggerFactory.CreateLogger(GetType());
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

        public void SetUri(Uri uri)
        {
            _rabbitConnectionFactory.Uri = uri;
        }

        protected string GetDefaultHostName()
        {
            string temp;

            try
            {
                temp = Dns.GetHostName();
                _logger.LogDebug($"Using hostname [{temp}] for hostname.");
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "Could not get host name, using 'localhost' as default value");
                temp = "localhost";
            }

            return temp;
        }

        protected void SetPublisherConnectionFactory(AbstractConnectionFactory publisherConnectionFactory)
        {
            _publisherConnectionFactory = publisherConnectionFactory;
        }
    }
}
