using System;

namespace Spring.Amqp.Rabbit.Events
{
    public class ConnectionBlockedEventArgs : EventArgs
    {
        public ConnectionBlockedEventArgs(string reason)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }
}
