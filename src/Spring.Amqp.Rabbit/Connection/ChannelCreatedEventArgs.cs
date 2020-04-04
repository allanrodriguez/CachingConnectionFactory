using RabbitMQ.Client;
using System;

namespace Spring.Amqp.Rabbit.Connection
{
    public class ChannelCreatedEventArgs : EventArgs
    {
        public IModel Channel { get; set; }

        public bool Transactional { get; set; }
    }
}
