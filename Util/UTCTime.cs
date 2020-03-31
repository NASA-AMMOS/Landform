using System;
using System.Threading;
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

    public class Stamped<T> where T : new()
    {
        public readonly T Value;
        
        private long _timestamp; //ms since UTC epoch
        public long Timestamp { get { return Interlocked.Read(ref _timestamp); } }
        
        public Stamped(T obj, long now)
        {
            this.Value = obj;
            this._timestamp = now;
        }
        
        public Stamped(long now) : this(new T(), now) { }
        
        public Stamped(T obj) : this(obj, (long)UTCTime.NowMS()) { }
        
        public Stamped() : this(new T()) { }
        
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
