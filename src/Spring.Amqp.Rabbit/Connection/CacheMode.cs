namespace Spring.Amqp.Rabbit.Connection
{
    public enum CacheMode
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
}
