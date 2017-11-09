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
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2.DataModel;

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
        IAmazonDynamoDB DBClient { get; set; }
        DynamoDBContext DBContext { get; set; }

        public Function()
        {
            SQSClient = new AmazonSQSClient();
            DBClient = new AmazonDynamoDBClient();
            DBContext = new DynamoDBContext(DBClient, new DynamoDBContextConfig { TableNamePrefix = Environment.GetEnvironmentVariable("TABLE_PREFIX") });
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

                string parentMeshName = record.Dynamodb.NewImage["parent_mesh_name"].S;

                //Look up all children for this parent tile 
                IEnumerable<ChildTile> children = await ChildTile.FindAll(DBContext, parentMeshName);

                //Look up this parent tile to check how many children it should have 
                ParentTile parent = await ParentTile.Find(DBContext, parentMeshName);

                //if #children = #total children, send message with extensions of all children in body of message 
                if (parent.NumChildren == children.Count())
                {
                    await sendMessage(parent.Bucket, parentMeshName, Convert.ToString(parent.NumChildren), children);
                }
            }

            context.Logger.LogLine("Stream processing complete.");
            return 0;
        }

        //Add this tile processing request to the queue with this prefix
        private async Task<string> sendMessage(string bucket, string parentPath, string numChildren, IEnumerable<ChildTile> children)
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
                MessageBody = "{}",
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
