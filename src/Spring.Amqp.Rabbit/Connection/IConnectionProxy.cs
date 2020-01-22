namespace Spring.Amqp.Rabbit.Connection
{
    /// <summary>
    /// Subinterface of <see cref="IConnection"/> to be implemented by Connection proxies. Allows access to the
    /// underlying target Connection.
    /// </summary>
    public interface IConnectionProxy : IConnection
    {
        IConnection TargetConnection { get; }
    }
}
