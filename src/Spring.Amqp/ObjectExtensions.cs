using System;
using System.Globalization;

namespace Spring.Amqp
{
    public static class ObjectExtensions
    {
        public static string GetIdentityHexString(this object thisObject)
        {
            if (thisObject == null) throw new ArgumentNullException(nameof(thisObject));

            return thisObject.GetHashCode().ToString("x", CultureInfo.InvariantCulture);
        }
    }
}
