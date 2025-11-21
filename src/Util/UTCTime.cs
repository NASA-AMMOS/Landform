using System;
using System.Threading;

namespace JPLOPS.Util
{
    public class UTCTime
    {
        public readonly static DateTime EPOCH = new DateTime(1970, 1, 1);

        public static TimeSpan SinceEpoch()
        {
            return DateTime.UtcNow - EPOCH;
        }

        public static DateTime SecondsSinceEpochToDate(double sec)
        {
            return MSSinceEpochToDate(sec * 1e3);
        }

        public static DateTime MSSinceEpochToDate(double ms)
        {
            return MSSinceEpochToDate((long)ms);
        }
            
        public static DateTime MSSinceEpochToDate(long ms)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).DateTime;
        }

        public static long DateToMSSinceEpoch(DateTime date)
        {
            return (long)(date.ToUniversalTime().Subtract(EPOCH).TotalMilliseconds);
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

    public class Stamped<T>
    {
        public readonly T Value;
        
        private long _timestamp; //ms since UTC epoch
        public long Timestamp { get { return Interlocked.Read(ref _timestamp); } }
        
        public Stamped(T obj, long now)
        {
            this.Value = obj;
            this._timestamp = now;
        }
        
        public Stamped(T obj) : this(obj, (long)UTCTime.NowMS()) { }
        
        public void Touch(long now)
        {
            Interlocked.Exchange(ref _timestamp, now);
        }
        
        public void Touch()
        {
            Touch((long)UTCTime.NowMS());
        }
    }
}
