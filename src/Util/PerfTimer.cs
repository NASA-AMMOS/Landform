using System.Diagnostics;

namespace JPLOPS.Util
{
    public class PerfTimer
    {
        public long ElapsedMicroseconds { get { return stopwatch.ElapsedTicks / ticksPerMicrosecond; } }

        public string HMS { get { return Fmt.HMS(this); } }

        public string HMSR
        {
            get
            {
                string hms = Fmt.HMS(this);
                Restart();
                return hms;
            }
        }
        
        private Stopwatch stopwatch;
        private long ticksPerMicrosecond;

        public PerfTimer(bool start = true)
        {
            stopwatch = Stopwatch.StartNew();
            ticksPerMicrosecond = Stopwatch.Frequency / (1000L*1000L);
        }

        public long Stop()
        {
            stopwatch.Stop();
            return ElapsedMicroseconds;
        }

        public long Start()
        {
            var us = ElapsedMicroseconds;
            stopwatch.Start();
            return us;
        }

        public long Restart()
        {
            long us = ElapsedMicroseconds;
            stopwatch.Restart();
            return us;
        }
    }
}
