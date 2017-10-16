using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

using Newtonsoft.Json;

using Amazon.Lambda.Core;
using Amazon.Lambda.DynamoDBEvents;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.DynamoDBv2.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace LambdaDynamoProcessing
{

    //Object types for deserializing Dynamo stream JSON 
    public class Record
    {
        public Keys keys;
        public Image NewImage;
        public Image OldImage;
    }

    public class Image
    {
        public Value mesh_name;
        public Value bucket;
        public Value child0;
        public Value child1;
        public Value child2;
        public Value child3;
    }

    public class Keys
    {
        public Value mesh_name;
    }

    public class Value
    {
        public string S; //string value of a field
    }

    //This class processes dynamoDB records. 
    public class Function
    {
        private static readonly JsonSerializer _jsonSerializer = new JsonSerializer();

        IAmazonSQS SQSClient { get; set; }

        public Function()
        {
            SQSClient = new AmazonSQSClient();
        }

        public async Task<int> FunctionHandler(DynamoDBEvent dynamoEvent, ILambdaContext context)
        {
            context.Logger.LogLine($"Beginning to process {dynamoEvent.Records.Count} records...");

            foreach (var record in dynamoEvent.Records)
            {
                context.Logger.LogLine($"Event ID: {record.EventID}");

                if (record.EventName == Amazon.DynamoDBv2.OperationType.REMOVE)
                {
                    return 0; //we don't need to process remove events 
                }

                string streamRecordJson = SerializeStreamRecord(record.Dynamodb);
                context.Logger.LogLine($"DynamoDB Record:");
                context.Logger.LogLine(streamRecordJson);

                //Get civilized and actually deserialize this json 
                Record recordObj = new Record();
                JsonSerializer serializer = new JsonSerializer();
                serializer.Populate(new JsonTextReader(new StringReader(streamRecordJson)), recordObj);

                context.Logger.LogLine(recordObj.NewImage.mesh_name.S);

                //check if each child is in this update 
                if (recordObj.NewImage.child0 != null && recordObj.NewImage.child1 != null &&
                    recordObj.NewImage.child2 != null && recordObj.NewImage.child3 != null)
                {
                    context.Logger.LogLine("Ready to create parent");
                    await sendMessage(recordObj.NewImage.bucket.S, recordObj.keys.mesh_name.S, record.EventID);
                }
            }

            context.Logger.LogLine("Stream processing complete.");
            return 0;
        }

        private string SerializeStreamRecord(StreamRecord streamRecord)
        {
            using (var writer = new StringWriter())
            {
                _jsonSerializer.Serialize(writer, streamRecord);
                return writer.ToString();
            }
        }

        //Add this tile processing request to the queue with this prefix
        private async Task<string> sendMessage(string bucket, string parentPath, string id)
        {
            SendMessageRequest request = new SendMessageRequest
            {
                DelaySeconds = (int)TimeSpan.FromSeconds(5).TotalSeconds,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    {
                    "ParentPath", new MessageAttributeValue
                    {DataType = "String", StringValue = bucket + "/" + parentPath }
                    }
                },
                MessageBody = "I was started by DynamoDB Stream event with ID " + id,
                QueueUrl = Environment.GetEnvironmentVariable("SQS_URL")
            };
            SendMessageResponse response = await SQSClient.SendMessageAsync(request);
            LambdaLogger.Log("Sent message with MessageID " + response.MessageId);
            //TODO check HTTP status code of the response 

            return response.MessageId;
        }
    }
}
