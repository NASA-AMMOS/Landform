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
        public static T Run<T>(Func<T> cloudOp, ILog logger=null, int initialWaitTime = 1000, double waitScaling = 2, int maxWait = 32000)
        {
            try
            {
                //Console.WriteLine("Attempting to run " + cloudOp.ToString());
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
                    if (logger != null)
                    {
                        logger.Info("Encounted througput exception" + e.ToString());
                        logger.Info("Waiting " + initialWaitTime + " ms before retrying...");
                    }
                    System.Threading.Thread.Sleep(initialWaitTime);
                    return Run(cloudOp, logger, (int)(initialWaitTime * waitScaling), waitScaling, maxWait);
                }
            }
        }
    }
}
