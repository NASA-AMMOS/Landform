using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace OPS.Util
{
    public class Histogram
    {
        private double bucketSize;
        private string objectName;
        private string valueName;

        private ConcurrentDictionary<int, int> buckets = new ConcurrentDictionary<int, int>();
        
        public Histogram(double bucketSize, string objectName, string valueName)
        {
            this.bucketSize = bucketSize;
            this.objectName = objectName;
            this.valueName = valueName;
        }
        
        public void Add(double value)
        {
            int bucket = (int)(value / bucketSize);
            buckets.AddOrUpdate(bucket, _ => 1, (_, count) => count + 1);
        }
        
        public void Dump(ILogger logger)
        {
            foreach (var key in buckets.Keys.OrderBy(n => n))
            {
                logger.LogInfo("{0} {1} with {2} to {3} {4}",
                               buckets[key], objectName, key * bucketSize, (key + 1) * bucketSize, valueName);
            }
        }
    }
}
