using Spring.Amqp.Rabbit.Connection;
using System;
using Xunit;

namespace Spring.Amqp.Rabbit.UnitTests.Connection
{
    public class CachingConnectionFactoryTests
    {
        [Fact]
        public void ChannelCacheSize_SetValueLessThan1_ThrowsArgumentOutOfRangeException()
        {
            var factory = new CachingConnectionFactory();

            Assert.Throws<ArgumentOutOfRangeException>(nameof(CachingConnectionFactory.ChannelCacheSize),
                () => factory.ChannelCacheSize = 0);
        }

        [Fact]
        public void ChannelCacheSize_SetValueGreaterThanOrEqualTo1_SavesValue()
        {
            const int expectedChannelCacheSize = 1;

            var factory = new CachingConnectionFactory { ChannelCacheSize = expectedChannelCacheSize };

            Assert.Equal(expectedChannelCacheSize, factory.ChannelCacheSize);
        }
    }
}
