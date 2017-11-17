using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.CloudWatch.Model;
using Amazon.CloudWatch;

namespace OPS.Cloud
{
    /// <summary>
    /// Names of custom CloudWatch metrics 
    /// </summary>
    public class MetricNames
    {
        public const string MESSAGES_RECIEVED = "MessagesRecieved";
        public const string MESSAGES_FAILED = "MessagesFailed";
        public const string MESSAGES_SUCCEEDED = "MessagesSucceeded";
    }

    /// <summary>
    /// Helper class for workers publishing custom cloudwatch metrics
    /// </summary>
    public class CloudWatchMetric
    {
        private IAmazonCloudWatch CWClient;

        //Cloudwatch namespace 
        private const string Namespace = "Pipeline";

        public CloudWatchMetric(IAmazonCloudWatch CWClient)
        {
            this.CWClient = CWClient;
        }

        /// <summary>
        /// Publish a metric. Worker pipeline metrics are identified by the pipeline name and worker instance. 
        /// </summary>
        /// <param name="value"></param>
        /// <param name="pipelineName"></param>
        /// <param name="instanceId"></param>
        /// <returns></returns>
        public System.Net.HttpStatusCode Publish(string metricName, double value, string pipelineName, string instanceId)
        {
            var CWResponse = CWClient.PutMetricData(new PutMetricDataRequest
            {
                MetricData = new List<MetricDatum> { new MetricDatum
                {
                    MetricName = metricName,
                    Unit = StandardUnit.Count,
                    Value = value,
                    Dimensions = new List<Dimension> {
                        new Dimension {Name = "OwnerName", Value = pipelineName},
                        new Dimension {Name = "Instance", Value = instanceId }
                    }
                } },
                Namespace = "Pipeline"
            });

            return CWResponse.HttpStatusCode;
        }
    }
}
