using System;
using System.Collections.Generic;

namespace Spring.Amqp
{
    public static class ListExtensions
    {
        private static readonly Random Rng = new Random();

        public static void Shuffle<T>(this IList<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));

            for (var i = list.Count - 1; i > 0; --i)
            {
                var j = Rng.Next(i + 1);

                var value = list[j];
                list[j] = list[i];
                list[i] = value;
            }
        }
    }
}
