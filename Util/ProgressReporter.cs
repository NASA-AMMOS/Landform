using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Util
{
    public class ProgressReporter
    {

        public delegate void Report(int v);

        DateTime lastReport = new DateTime();
        Report ReportFunction;
        int rate;
        int? lastValue;

        public ProgressReporter(int rate, Report f)
        {
            this.rate = rate;
            this.ReportFunction = f;
        }

        public void Update(int value)
        {
            if(lastValue == null || lastValue.Value != value)
            {
                if((DateTime.Now - lastReport).TotalSeconds >= rate)
                {
                    lastReport = DateTime.Now;
                    lastValue = value;
                    ReportFunction(value);
                }
            }            
        }
    }
}
