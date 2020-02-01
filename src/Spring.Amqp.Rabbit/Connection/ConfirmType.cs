namespace Spring.Amqp.Rabbit.Connection
{
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
}
