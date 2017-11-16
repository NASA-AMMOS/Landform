using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Lambda.LambdaUtil
{
    /// <summary>
    /// Parent tile metadata. Stores number of children that have been uploaded (repeat uploads continue to incrememnt counter)
    /// Not versioned, so "Create" will always succeed but will not overwrite existing fields except NumChildren, 
    /// and a save will always succeed even if you are not editing the most recent version in the DB. 
    /// </summary>
    [DynamoDBTable("ParentTiles")]
    public class ParentTile
    {
        /// <summary>
        /// bucket/key of the parent mesh within S3, without the extension 
        /// </summary>
        [DynamoDBHashKey]
        [DynamoDBProperty("mesh_name")]
        public string MeshName;

        //How many children are nececary for the construction of this parent tile? 
        //At least one child is always needed. NumChildren = 0 means that NumChildren has not yet been specified 
        [DynamoDBProperty("num_children")]
        public int? NumChildren;

        /// <summary>
        /// Number of children in ChildTiles for this parent. May sometimes overcount, never undercounts
        /// </summary>
        [DynamoDBProperty("num_children_present")]
        public int? NumChildrenPresent; 

        //required by aws sdk, should not be used otherwise
        public ParentTile() { }

        protected ParentTile(string meshName)
        {
            MeshName = meshName;
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

        /// <summary>
        /// Create a new parent tile entry. If one already exists, this will succeed but not overwrite any existing values
        /// </summary>
        /// <param name="context"></param>
        /// <param name="meshName">Bucket and key, minus extension, of parent tile in S3. Format bucket/key </param>
        /// <param name="numChildren"></param>
        /// <returns></returns>
        public static async Task<ParentTile> Create(DynamoDBContext context, string meshName, int numChildren)
        {
            //try to create new parent tile without setting numChildren 
            ParentTile newTile = new ParentTile(meshName);
            newTile.NumChildren = numChildren;
            await context.SaveAsync(newTile, new DynamoDBOperationConfig() { IgnoreNullValues = true });
            return newTile;
        }
        
        //This is a consistent read because DynamoProcessing lambda needs to compare most recent versions of parent and child tables
        public static async Task<ParentTile> Find(DynamoDBContext context, string meshName)
        {
            return await context.LoadAsync<ParentTile>(meshName, new DynamoDBOperationConfig { ConsistentRead = true });
        }
    }
}
