using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2.DataModel;

using Lambda.LambdaUtil;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace Lambda.LambdaS3TileIntake
{
    //This uploads metadata to dynamoDB
    public class Function
    {
        private const string DB_PRIMARY_KEY = "mesh_name";

        private IAmazonS3 S3Client { get; set; }
        private IAmazonDynamoDB DBClient { get; set; }
        private DynamoDBContext DBContext { get; set; }

        /// <summary>
        /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
        /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
        /// region the Lambda function is executed in.
        /// </summary>
        public Function()
        {
            S3Client = new AmazonS3Client();
            DBClient = new AmazonDynamoDBClient();
            DBContext = new DynamoDBContext(DBClient, new DynamoDBContextConfig { TableNamePrefix = Environment.GetEnvironmentVariable("TABLE_PREFIX") });
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
        public async Task<string> FunctionHandler(S3Event evnt, ILambdaContext context) 
        {
            var s3Event = evnt.Records?[0].S3;
            if (s3Event == null)
            {
                return null;
            }

            //decide whether we should process parent tile
            string key = s3Event.Object.Key;
            //string prefix = Path.GetFileNameWithoutExtension(key).Substring(0, key.Length-1);
            string prefix = key.Substring(0, key.Length - 5);
            string suffix = key.Substring(key.Length - 5, 1);
            string file_ending = key.Substring(key.Length - 4, 4);
            string bucket = s3Event.Bucket.Name;
            LambdaLogger.Log("Prefix: " + prefix + "\nSuffix: " + suffix + "\nFile ending: " + file_ending + "\nBucket: " + bucket);

            if (file_ending != ".obj")
            {
                return "I only like object files";
            }

            //in a world where no one supports .net ... 
            //Using low-level API to get access to ADD operations 
            // TODO I can't find documentation on concurrent ADD operations. I *assume* it's ok??? 

            // If parent does not exist, create parent 
            await ParentTile.CreateIfNotPresent(DBContext, prefix, bucket, TableNames.HARDCODED_4);

            // Put this child tile into the db 
            await ChildTile.Create(DBContext, prefix, suffix);

            return "success";
        }
    }
}
