using System;

namespace JPLOPS.MathExtensions
{
    /// <summary>
    /// Computes basic statistics for a set of numbers
    /// </summary>
    public class RunningAverage
    {
        int n;
        double oldM, newM, oldS, newS;
        double min, max;
        
        public RunningAverage()
        {
            n = 0;
            min = 0;
            max = 0;
        }

        public void Clear()
        {
            n = 0;
        }

        /// <summary>
        /// Add a new value to be included in this average
        /// </summary>
        /// <param name="x"></param>
        public void Push(double x)
        {
            n++;
            // See Knuth TAOCP vol 2, 3rd edition, page 232
            if (n == 1)
            {
                oldM = newM = x;
                oldS = 0.0;
                min = max = x;
            }
            else
            {
                min = Math.Min(x, min);
                max = Math.Max(x, max);
                newM = oldM + (x - oldM) / n;
                newS = oldS + (x - oldM) * (x - newM);
                // set up for next iteration
                oldM = newM;
                oldS = newS;
            }
        }
        
        public int Count
        {
            get { return n; }
        }

        public double Mean
        {
            get { return (n > 0) ? newM : 0.0; }
        }

        public double Variance
        {
            get { return ((n > 1) ? newS / (n - 1) : 0.0); }
        }

        public double StandardDeviation
        {
            get { return Math.Sqrt(Variance); }
        }

        public double Min
        {
            get { return min; }
        }

        public double Max
        {
            get { return max; }
        }
    }
}
