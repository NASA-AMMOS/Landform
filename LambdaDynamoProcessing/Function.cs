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

                if (record.EventName == Amazon.DynamoDBv2.OperationType.REMOVE || !record.EventSourceArn.Contains("ParentTiles"))
                {
                    continue; //we don't need to process remove events 
                }

                string streamRecordJson = SerializeStreamRecord(record.Dynamodb);
                context.Logger.LogLine($"DynamoDB Record:");
                context.Logger.LogLine(streamRecordJson);

                string parentMeshName;
                int numDesired;
                int numReality;

                //We care about modifications where num_children or num_children_present is changed, eg when a user adds metadata, which we DO care about 
                if (
                    //basics are correct...
                    (record.EventSourceArn.Contains("ParentTiles") && 
                    record.EventName == OperationType.MODIFY &&
                    record.Dynamodb.NewImage.ContainsKey("num_children_present")) &&
                    //and a relevant value has changed...
                    (record.Dynamodb.NewImage["num_children"] != record.Dynamodb.OldImage["num_children"] || //number of desired children changed
                    (record.Dynamodb.OldImage.ContainsKey("num_children_present") && //number of present children changed
                    record.Dynamodb.NewImage["num_children_present"] != record.Dynamodb.OldImage["num_children_present"]))
                    )
                {
                    parentMeshName = record.Dynamodb.NewImage["mesh_name"].S;
                    numReality = Convert.ToInt32(record.Dynamodb.NewImage["num_children_present"].N);
                    numDesired = Convert.ToInt32(record.Dynamodb.NewImage["num_children"].N);
                }
                else
                {
                    continue; //not ready to process
                }

                if (numReality < numDesired) //overcounting is possible but undercounting is not
                {
                    continue;
                }

                //Look up all children for this parent tile, because it is possible that the counter overcounted
                IEnumerable<ChildTile> children = await ChildTile.FindAll(DBContext, parentMeshName);

                //Look up this parent tile to check how many children it should have 
                //ParentTile parent = await ParentTile.Find(DBContext, parentMeshName);

                //if #children = #total children, send message with extensions of all children in body of message 
                if (numDesired != 0 && numDesired == children.Count())
                {
                    await sendMessage("landlords-dev", parentMeshName, Convert.ToString(numDesired), children);
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
        private async Task<string> sendMessage(string bucket, string parentPath, string numChildren, IEnumerable<ChildTile> children)
        {
            //Get json of child extensions for worker
            List<string> extensions = new List<string>();
            foreach(ChildTile child in children)
            {
                extensions.Add(child.ChildExtension);
            }
            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None };
            string extensionsMsg = JsonConvert.SerializeObject(extensions, typeof(object), settings);

            //Send message
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
                MessageBody = extensionsMsg,
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
