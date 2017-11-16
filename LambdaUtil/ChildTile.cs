using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2;

namespace Lambda.LambdaUtil
{
    /// <summary>
    /// A ChildTile for the mesh tiling pipeline. 
    /// ChildTiles are not versioned. A Create will always succeed, but will not overwrite existing values 
    /// and a save will always succeed even if you are not editing the most recent version in the DB. 
    /// </summary>
    [DynamoDBTable("ChildTiles")]
    public class ChildTile
    {
        /// <summary>
        /// The key of the parent mesh within S3, without the extension 
        /// </summary>
        [DynamoDBHashKey]
        [DynamoDBProperty("parent_mesh_name")]
        public string MeshName;

        [DynamoDBRangeKey]
        [DynamoDBProperty("child_extension")]
        public string ChildExtension;

        //required by aws sdk, should not be used otherwise 
        public ChildTile() { }

        protected ChildTile(string meshName, string extension)
        {
            MeshName = meshName;
            ChildExtension = extension;
        }

        /// <summary>
        /// Save a new child tile. 
        /// If an existing child is present, this will not overwrite its values, but will still succeed
        /// </summary>
        /// <param name="context"></param>
        /// <param name="meshName"></param>
        /// <param name="bucket"></param>
        /// <param name="numChildren"></param>
        /// <returns></returns>
        public static async Task<ChildTile> Create(DynamoDBContext context, string meshName, string extension)
        {
            ChildTile newTile = new ChildTile(meshName, extension);
            await context.SaveAsync(newTile, new DynamoDBOperationConfig { IgnoreNullValues = true});
            return newTile;
        }

        //bucket included here because it should be a sort key
        //This is a consistent read because DynamoProcessing lambda needs to compare most recent versions of parent and child tables
        public static async Task<ChildTile> Find(DynamoDBContext context, string meshName, string extension)
        {
            return await context.LoadAsync<ChildTile>(meshName, extension, new DynamoDBOperationConfig { ConsistentRead = true });
        }
         /// <summary>
         /// Get all child tiles with this parent tile key
         /// </summary>
         /// <param name="context"></param>
         /// <param name="meshName"></param>
         /// <returns></returns>
        public static async Task<IEnumerable<ChildTile>> FindAll(DynamoDBContext context, string meshName)
        {
            AsyncSearch<ChildTile> search = context.QueryAsync<ChildTile>(meshName, new DynamoDBOperationConfig { ConsistentRead = true });
            return await search.GetRemainingAsync();
        }
    }
}
