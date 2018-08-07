using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;

namespace OPS.Cloud
{
    public static class ThroughputManager
    {
        static ILog logger = LogManager.GetLogger(typeof(ThroughputManager));

        public static T Run<T>(Func<T> cloudOp, int initialWaitTime = 1000, double waitScaling = 2, int maxWait = 32000)
        {
            try
            {
                return cloudOp();
            }
            catch (ProvisionedThroughputExceededException e)
            {
                if (initialWaitTime > maxWait)
                {
                    throw new CloudException("Wait time of " + initialWaitTime + " exceeded max wait of " + maxWait);
                }
                else
                {
                    logger.Info("Encounted througput exception" + e.ToString());
                    logger.Info("Waiting " + initialWaitTime + " ms before retrying...");          
                    System.Threading.Thread.Sleep(initialWaitTime);
                    return Run(cloudOp, (int)(initialWaitTime * waitScaling), waitScaling, maxWait);
                }
            }
        }
    }
}
