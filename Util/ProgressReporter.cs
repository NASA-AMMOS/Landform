using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Util
{

    public abstract class ProgressReporterBase
    {
        protected DateTime lastReport = new DateTime();
        protected int reportInterval;

    }

    /// <summary>
    /// Class for rate limited reporting
    /// </summary>
    public class ProgressReporter : ProgressReporterBase
    {
        public delegate void Report();
        Report ReportFunction;

        /// <summary>
        /// Create a progress reporter that will call the block "f" at most once ever
        /// "intervalInSeconds" regardless of how frequently "update" is called
        /// </summary>
        /// <param name="intervalInSeconds"></param>
        /// <param name="f"></param>
        public ProgressReporter(int intervalInSeconds, Report f)
        {
            this.reportInterval = intervalInSeconds;
            this.ReportFunction = f;
        }

        /// <summary>
        /// Called every time an update should be considered for reporting.  Will trigger
        /// reports at most once per reporting interval
        /// </summary>
        public void Update()
        {
            if ((DateTime.Now - lastReport).TotalSeconds >= reportInterval)
            {
                lastReport = DateTime.Now;
                ReportFunction();
            }
        }
    }

    /// <summary>
    /// Class for rate limited reporting with a value
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ProgressReporter<T> : ProgressReporterBase
    {
        public delegate void Report<TT>(TT v);

        Report<T> ReportFunction;
        T lastValue;
        bool initialized = false;

        /// <summary>
        /// Create a progress reporter that will call the block "f" at most once ever
        /// "intervalInSeconds" regardless of how frequently "update" is called
        /// </summary>
        public ProgressReporter(int intervalInSeconds, Report<T> f)
        {
            this.reportInterval = intervalInSeconds;
            this.ReportFunction = f;
        }

        /// <summary>
        /// Called every time an update should be considered for reporting.  Will trigger
        /// reports at most once per reporting interval.  Additionally, will only report if
        /// the new value is different from the last one reported
        /// </summary>
        public void Update(T value)
        {
            if(initialized == false || !lastValue.Equals(value))
            {
                initialized = true;
                if ((DateTime.Now - lastReport).TotalSeconds >= reportInterval)
                {
                    lastReport = DateTime.Now;
                    lastValue = value;
                    ReportFunction(value);
                }
            }            
        }
    }
}
