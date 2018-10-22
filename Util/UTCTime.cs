using System;
using System.Collections;
using System.Collections.Generic;

namespace OPS.Util
{
    public class UTCTime
    {
        public static TimeSpan SinceEpoch()
        {
            return DateTime.UtcNow - new DateTime(1970, 1, 1);
        }

        public static double Now()
        {
            return SinceEpoch().TotalSeconds;
        }

        public static double NowMS()
        {
            return SinceEpoch().TotalMilliseconds;
        }
    }
}
