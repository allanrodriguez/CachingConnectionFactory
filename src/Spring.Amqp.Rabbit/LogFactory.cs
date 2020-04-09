using Microsoft.Extensions.Logging;

namespace Spring.Amqp.Rabbit
{
    public static class LogFactory
    {
        private static readonly LoggerFactory Factory = new LoggerFactory();

        public static ILogger<TCategoryName> GetLogger<TCategoryName>()
        {
            return Factory.CreateLogger<TCategoryName>();
        }
    }
}
