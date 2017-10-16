using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace LambdaS3PutProcessing
{
    //This uploads metadata to dynamoDB
    public class Function
    {
        private const string DB_PRIMARY_KEY = "mesh_name";

        IAmazonS3 S3Client { get; set; }
        Table table { get; set; }

        /// <summary>
        /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
        /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
        /// region the Lambda function is executed in.
        /// </summary>
        public Function()
        {
            S3Client = new AmazonS3Client();
        }

        /// <summary>
        /// Constructs an instance with a preconfigured S3 client. This can be used for testing the outside of the Lambda environment.
        /// </summary>
        /// <param name="s3Client"></param>
        public Function(IAmazonS3 s3Client)
        {
            this.S3Client = s3Client;
        }

        /// <summary>
        /// This method is called for every Lambda invocation. This method takes in an S3 event object and can be used 
        /// to respond to S3 notifications.
        /// </summary>
        /// <param name="evnt"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<string> FunctionHandler(S3Event evnt, ILambdaContext context) //TODO return type?? 
        {
            var s3Event = evnt.Records?[0].S3;
            if (s3Event == null)
            {
                return null;
            }

            //decide whether we should process parent tile
            string key = s3Event.Object.Key;
            string prefix = key.Substring(0, key.Length - 5);
            int suffix = Convert.ToInt32(key.Substring(key.Length - 5, 1));
            string file_ending = key.Substring(key.Length - 4, 4);
            string bucket = s3Event.Bucket.Name;
            LambdaLogger.Log("Prefix: " + prefix + "\nSuffix: " + suffix + "\nFile ending: " + file_ending + "\nBucket: " + bucket);

            if (file_ending != ".obj")
            {
                return "I only like object files";
            }


            Table table = GetTableObject(Environment.GetEnvironmentVariable("DB_NAME"));
            if (table == null)
            {
                LambdaLogger.Log("Failed to connect to dynamo DB table");
            }
            //upload metadata to DynamoDB
            Document document = new Document();
            document[DB_PRIMARY_KEY] = prefix;
            document["child" + Convert.ToString(suffix)] = key;
            document["updated"] = DateTime.UtcNow;
            document["bucket"] = bucket;

            try
            {
                await table.UpdateItemAsync(document);
                LambdaLogger.Log("\nPutItem succeeded.\n");
            }
            catch (Exception ex)
            {
                LambdaLogger.Log("\n Error: Table.PutItem failed because: " + ex.Message);
            }
            return "success";
        }



        /// <summary>
        /// For getting a table on the initialization of the Lambda
        /// from http://docs.aws.amazon.com/amazondynamodb/latest/developerguide/GettingStarted.NET.03.html
        /// </summary>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public static Table GetTableObject(string tableName)
        {
            AmazonDynamoDBConfig ddbConfig = new AmazonDynamoDBConfig();
            ddbConfig.RegionEndpoint = Amazon.RegionEndpoint.USWest1;
            AmazonDynamoDBClient client;
            try
            {
                client = new AmazonDynamoDBClient(ddbConfig);
            }
            catch (Exception ex)
            {
                LambdaLogger.Log("\n Error: failed to create a DynamoDB client; " + ex.Message);
                return (null);
            }

            // Now, create a Table object for the specified table
            Table table = null;
            try
            {
                table = Table.LoadTable(client, tableName);
            }
            catch (Exception ex)
            {
                LambdaLogger.Log("\n Error: failed to load the " + tableName + " table; " + ex.Message);
                return (null);
            }
            return (table);
        }
    }
}
