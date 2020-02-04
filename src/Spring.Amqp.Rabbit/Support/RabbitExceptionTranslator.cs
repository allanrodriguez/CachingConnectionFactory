using RabbitMQ.Client.Exceptions;
using System;
using System.IO;

namespace Spring.Amqp.Rabbit.Support
{
    public static class RabbitExceptionTranslator
    {
        public static Exception ConvertRabbitAccessException(Exception ex)
        {
            if (ex == null) throw new ArgumentNullException(nameof(ex), "Exception must not be null.");

            if (ex is AmqpException)  return ex as AmqpException;

            if (ex is OperationInterruptedException)
                return new AmqpConnectException(ex as OperationInterruptedException);
            
            if (ex is PossibleAuthenticationFailureException) return new AmqpAuthenticationException(ex);

            if (ex is IOException) return new AmqpIOException(ex as IOException);
            
            if (ex is TimeoutException) return new AmqpTimeoutException(ex);

            return new UncategorizedAmqpException(ex);
        }
    }
}
