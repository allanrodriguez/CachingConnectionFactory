using Microsoft.Extensions.Logging;
using System;

namespace Spring.Amqp.Rabbit.Support
{
    /// <summary>
    /// For components that support customization of the logging of certain events, users can provide an implementation
    /// of this interface to modify the existing logging behavior.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="message">A message that the caller suggests should be included in the log.</param>
    /// <param name="e">An exception; may be null.</param>
    /// <param name="args">An object array that contains zero or more objects to format in the message.</param>
    public delegate void ConditionalExceptionLogger(ILogger logger, string message, Exception e, params object[] args);
}
