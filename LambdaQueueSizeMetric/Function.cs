using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.Lambda.Core;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.SQS;
using Amazon.SQS.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace LambdaQueueSizeMetric
{
    public class Function
    {
        IAmazonCloudWatch CWClient;
        IAmazonSQS SQSClient;

        /// <summary>
        /// Constructor for a lambda instance. Multiple handler calls may reuse this. 
        /// </summary>
        public Function()
        {
            CWClient = new AmazonCloudWatchClient();
            SQSClient = new AmazonSQSClient();
        }

        /// <summary>
        /// Get queue size and write it to a cloudwatch metric. 
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<string> FunctionHandler(Object scheduledEvent, ILambdaContext context)
        {
            //get queue size 
            var response = await SQSClient.GetQueueAttributesAsync(new GetQueueAttributesRequest
            {
                QueueUrl = Environment.GetEnvironmentVariable("JOB_QUEUE"),
                AttributeNames = new List<string>() { "ApproximateNumberOfMessages" }
            });

            //publish custom metric
            var CWResponse = CWClient.PutMetricDataAsync(new PutMetricDataRequest
            {
                MetricData = new List<MetricDatum> { new MetricDatum
                {
                    MetricName = "SQSSize",
                    Unit = StandardUnit.None,
                    Value = response.ApproximateNumberOfMessages,
                    Dimensions = new List<Dimension> {
                        new Dimension {Name = "OwnerName", Value = Environment.GetEnvironmentVariable("PIPELINE_NAME")}, //what pipeline does this metric belong to? 
                    }
                } },
                Namespace = "Pipeline"
            });

            return Convert.ToString(response.ApproximateNumberOfMessages);
        }
    }
}
