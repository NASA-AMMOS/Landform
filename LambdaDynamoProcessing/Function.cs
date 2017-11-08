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

using Lambda.LambdaUtil; 

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace Lambda.LambdaDynamoProcessing
{
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

                context.Logger.LogLine(record.Dynamodb.NewImage[TableNames.PARENT_MESH_ID_FIELD].S);

                //check the length of the children list in this update 
                //Dictionary<string, AttributeValue> childmap = record.Dynamodb.NewImage[TableNames.CHILDREN].M;


                //check if all children are in this update 
                if (record.Dynamodb.NewImage.ContainsKey(TableNames.CHILDREN) &&
                    record.Dynamodb.NewImage.ContainsKey(TableNames.NUM_CHILDREN) &&
                    record.Dynamodb.NewImage[TableNames.CHILDREN].SS.Count == Convert.ToInt32(record.Dynamodb.NewImage[TableNames.NUM_CHILDREN].N))
                {
                    context.Logger.LogLine("Ready to create parent");
                    await sendMessage(record.Dynamodb.NewImage[TableNames.BUCKET].S, record.Dynamodb.NewImage[TableNames.PARENT_MESH_ID_FIELD].S,
                        record.Dynamodb.NewImage[TableNames.NUM_CHILDREN].N, record.EventID);
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
        private async Task<string> sendMessage(string bucket, string parentPath, string numChildren, string id)
        {
            SendMessageRequest request = new SendMessageRequest
            {
                DelaySeconds = (int)TimeSpan.FromSeconds(5).TotalSeconds,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    {
                    CreateParentTileMsgFields.PARENT_PATH, new MessageAttributeValue
                    {DataType = "String", StringValue = bucket + "/" + parentPath }
                    },
                    {
                    CreateParentTileMsgFields.NUM_CHILDREN, new MessageAttributeValue 
                    {DataType = "String", StringValue = numChildren } //No data types other than string currently supported
                    },
                    {
                    CreateParentTileMsgFields.MSG_TYPE_FIELD, new MessageAttributeValue
                    {DataType = "String", StringValue = PipelineMessageTypes.CREATE_PARENT_TILE_MSG } //No data types other than string currently supported
                    }
                },
                MessageBody = "I was started by DynamoDB Stream event with ID " + id,
                QueueUrl = Environment.GetEnvironmentVariable("JOB_QUEUE")
            };
            SendMessageResponse response = await SQSClient.SendMessageAsync(request);
            LambdaLogger.Log("Sent message with MessageID " + response.MessageId);

            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                //Something is wrong with the connection. Quit, another lambda will try again 
                throw new Exception("Problem sending message to SQS queue");
            }

            return response.MessageId;
        }
    }
}
