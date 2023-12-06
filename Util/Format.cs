using System;
using System.Diagnostics;

namespace JPLOPS.Util
{
    public class Fmt
    {
        public static string HMS(double ms)
        {
            if (ms < 1e3)
            {
                return string.Format("{0}ms", (int)ms);
            }
            else if (ms < 60 * 1e3)
            {
                double s = 1e-3 * ms;
                return string.Format("{0:F3}s", s);
            }
            else if (ms < 60 * 60 * 1e3)
            {
                int s = (int)(1e-3 * ms);
                return string.Format("{0}m{1}s", s / 60, s % 60);
            }
            else
            {
                int s = (int)(1e-3 * ms);
                return string.Format("{0}h{1}m{2}s", s / (60 * 60), (s / 60) % 60, s % 60);
            }
        }

        public static string HMS(PerfTimer pt)
        {
            return HMS(pt.ElapsedMicroseconds / 1000.0);
        }

        public static string HMS(Stopwatch sw)
        {
            return HMS(sw.ElapsedMilliseconds);
        }

        public static string KMG(double b, double k = 1e3)
        {
            if (Math.Abs(b) < k) return b.ToString("f0");
            else if (Math.Abs(b) < k*k) return string.Format("{0:f1}k", b/k);
            else if (Math.Abs(b) < k*k*k) return string.Format("{0:f1}M", b/(k*k));
            else return string.Format("{0:f1}G", b/(k*k*k));
        }

        public static string Bytes(double b)
        {
            return KMG(b, 1024);
        }
    }
}
