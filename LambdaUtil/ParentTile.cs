using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Lambda.LambdaUtil
{
    [DynamoDBTable("ParentTiles")]
    public class ParentTile
    {
        /// <summary>
        /// The key of the parent mesh within S3, without the extension 
        /// </summary>
        [DynamoDBHashKey]
        [DynamoDBProperty("mesh_name")]
        public string MeshName;

        //TODO this should be included in the key
        [DynamoDBProperty("bucket")]
        public string Bucket;

        //How many children are nececary for the construction of this parent tile? 
        //At least one child is always needed. NumChildren = 0 means that NumChildren has not yet been specified 
        [DynamoDBProperty("num_children")]
        public int? NumChildren;

        /// <summary>
        /// Number of children in ChildTiles for this parent. May sometimes overcount, never undercounts
        /// </summary>
        [DynamoDBProperty("num_children_present")]
        public int? NumChildrenPresent; 

        //[DynamoDBVersion]
        //public int? VersionNumber;

        //required by aws sdk, should not be used otherwise
        public ParentTile() { }

        protected ParentTile(string meshName, string bucket)
        {
            MeshName = meshName;
            Bucket = bucket;
        }

        /// <summary>
        /// Add one child to this parent. 
        /// Requires a parent that has been saved to the database. 
        /// Will always succeed, may overcount if Dynamo failure
        /// <param name="client">Requires client not context because this is a low-level op</param>
        /// </summary>
        public async Task<int> IncrementChildren(IAmazonDynamoDB client)
        {
            UpdateItemRequest request = new UpdateItemRequest()
            {
                TableName = Environment.GetEnvironmentVariable("TABLE_PREFIX") + "ParentTiles",
                Key = new Dictionary<string, AttributeValue>
                {
                    {"mesh_name", new AttributeValue(this.MeshName) }
                },
                UpdateExpression = "add num_children_present :num",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    {":num", new AttributeValue() { N = "1"} }
                },
                
            };

            UpdateItemResponse response = await client.UpdateItemAsync(request);

            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception("Could not increment DynamoDB value");
            }

            return 0;
        }

        public static async Task<ParentTile> FindOrCreate(DynamoDBContext context, string meshName, string bucket, int numChildren)
        {
            ParentTile tile = await CreateIfNotPresent(context, meshName, bucket, numChildren);
            if (tile != null)
            {
                return tile;
            }
            else return await Find(context, meshName);
        }

        /// <summary>
        /// Create a new parent tile entry IFF one does not already exist in the database. 
        /// If parent tile already exists, return null 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="meshName"></param>
        /// <param name="bucket"></param>
        /// <param name="numChildren"></param>
        /// <returns></returns>
        public static async Task<ParentTile> CreateIfNotPresent(DynamoDBContext context, string meshName, string bucket, int numChildren)
        {
            //try to create new parent tile without setting numChildren 
            ParentTile newTile = new ParentTile(meshName, bucket);
            try
            {
                await context.SaveAsync(newTile, new DynamoDBOperationConfig() { IgnoreNullValues = true });
            }
            catch (AmazonDynamoDBException e)
            {
                if (e.ErrorCode == "ConditionalCheckFailedException") return null; //if create fails another worker has already uploaded and updated this overlap
                else throw e; //unexpected error
            }

            //If our initial save succeeded, try to edit our parent tile. 
            //If two lambdas are trying to create simultaneously, only one of these edits will succeed
            newTile.NumChildren = numChildren;
            try
            {
                await context.SaveAsync(newTile, new DynamoDBOperationConfig() { IgnoreNullValues = true});
            }
            catch (AmazonDynamoDBException e)
            {
                if (e.ErrorCode == "ConditionalCheckFailedException") return null;
                else throw e;
            }

            //if save was successful, return ParentTile with correct version number so it can be saved
            return await context.LoadAsync<ParentTile>(newTile.MeshName, new DynamoDBOperationConfig { ConsistentRead = true });
        }
        
        //This is a consistent read because DynamoProcessing lambda needs to compare most recent versions of parent and child tables
        public static async Task<ParentTile> Find(DynamoDBContext context, string meshName)
        {
            return await context.LoadAsync<ParentTile>(meshName, new DynamoDBOperationConfig { ConsistentRead = true });
        }
    }
}
