using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2;

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

        [DynamoDBProperty("num_children")]
        public int? NumChildren;

        [DynamoDBVersion]
        public int? VersionNumber;

        //required by aws sdk, should not be used otherwise
        public ParentTile() { }

        protected ParentTile(string meshName, string bucket)
        {
            MeshName = meshName;
            Bucket = bucket;
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
                await context.SaveAsync(newTile);
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
                await context.SaveAsync(newTile);
            }
            catch (AmazonDynamoDBException e)
            {
                if (e.ErrorCode == "ConditionalCheckFailedException") return null;
                else throw e;
            }

            //if save was successful, return Overlap with correct version number so it can be saved
            return await context.LoadAsync<ParentTile>(newTile.MeshName, new DynamoDBOperationConfig { ConsistentRead = true });
        }

        //bucket included here because it should be a sort key
        //This is a consistent read because DynamoProcessing lambda needs to compare most recent versions of parent and child tables
        public static async Task<ParentTile> Find(DynamoDBContext context, string meshName)
        {
            return await context.LoadAsync<ParentTile>(meshName, new DynamoDBOperationConfig { ConsistentRead = true });
        }
    }
}
