using Amazon.DynamoDBv2.DataModel;
using Microsoft.Xna.Framework;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.TileServer
{
    [DynamoDBTable("TilingNode")]
    [DynamoDBReadCapacity(5, 50)]
    [DynamoDBWriteCapacity(5, 50)]
    public class TilingNode
    {
        [DynamoDBHashKey] //Partition key
        [DynamoDBProperty()]
        public string Id { get; set; }

        [DynamoDBRangeKey]
        public string ProjectName { get; set; }

        public string MeshUrl { get; set; }
        public string ImageUrl { get; set; }
        public string ParentId { get; set; }
        public List<string> ChildIds { get; set; }
        public string Bounds { get; set; }

        public TilingNode()
        {

        }

        /// <summary>
        /// Creates Project object locally.  
        /// </summary>
        /// <param name="name">Project names in the database must be unique</param>
        protected TilingNode(string id, TilingProject project, string meshUrl, string imageUrl, string parentId, List<string> childIds, BoundingBox bounds)
        {
            Id = id;
            ProjectName = project.Name;
            MeshUrl = meshUrl;
            ImageUrl = imageUrl;
            ParentId = parentId;
            ChildIds = childIds;
            Bounds = JsonHelper.ToJson(bounds);
        }


        public static TilingNode Create(DynamoDBContext context, string id, TilingProject project, string meshUrl, string imageUrl, string parentId, List<string> childIds, BoundingBox bounds)
        {
            TilingNode node = new TilingNode(id, project, meshUrl, imageUrl, parentId, childIds, bounds);
            context.Save(node, new DynamoDBOperationConfig() { IgnoreNullValues = true });
            return node;
        }
        

        public static TilingNode Find(DynamoDBContext context, TilingProject project, string id)
        {
            return context.Load<TilingNode>(id, project.Name);
        }


        public static IEnumerable<TilingNode> Find(DynamoDBContext context, TilingProject project)
        {
            return context.Scan<TilingNode>(
                new ScanCondition("ProjectName", Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, project.Name)
                );
        }

        public void Save(DynamoDBContext context)
        {
            context.Save(this, new DynamoDBOperationConfig() { IgnoreNullValues = true });
        }

        public BoundingBox GetBounds()
        {
            return (BoundingBox)JsonHelper.FromJson(this.Bounds);
        }


        public static SceneNode BuildTreeFromDatabase(DynamoDBContext context, TilingProject project)
        {
            var nodes = Find(context, project).ToList();
            Dictionary<string, SceneNode> idToNode = new Dictionary<string, SceneNode>();
            // Create all nodes
            foreach (var n in nodes)
            {
                SceneNode node = new SceneNode(n.Id);
                node.AddComponent(new NodeBounds(n.GetBounds()));
                idToNode.Add(node.Name, node);
            }
            // Connect parents and children
            SceneNode root = null;
            foreach (var n in nodes)
            {
                SceneNode node = idToNode[n.Id];
                if (n.ParentId == null)
                {
                    root = node;
                }
                else
                {
                    node.Transform.SetParent(idToNode[n.ParentId].Transform);
                }
            }
            return root;
        }
    }
}
