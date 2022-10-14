using System;

namespace JPLOPS.Util
{

    public abstract class ProgressReporterBase
    {
        protected DateTime? lastReportTime;
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
            if (lastReportTime == null ||  (DateTime.Now - lastReportTime.Value).TotalSeconds >= reportInterval)
            {
                lastReportTime = DateTime.Now;
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
        public delegate void Report(T v);

        Report ReportFunction;
        T lastReportedValue;

        /// <summary>
        /// Create a progress reporter that will call the block "f" at most once ever
        /// "intervalInSeconds" regardless of how frequently "update" is called
        /// </summary>
        public ProgressReporter(int intervalInSeconds, Report f)
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
            // Report iff
            // 1. lastReport is null (first time this has ever been called
            // 2. value is different from the last value that was reported AND the time since last report is greater than interval
            if (lastReportTime == null ||
               ((DateTime.Now - lastReportTime.Value).TotalSeconds >= reportInterval) && !value.Equals(lastReportedValue))
            {
                lastReportTime = DateTime.Now;
                lastReportedValue = value;
                ReportFunction(value);
            }            
        }
    }
}
