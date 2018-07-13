using Amazon.DynamoDBv2.DataModel;
using OPS.Cloud;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.TileServer
{


    [DynamoDBTable("TilingInput")]
    [DynamoDBReadCapacity(5, 50)]
    [DynamoDBWriteCapacity(5, 50)]
    public class TilingInput
    {
        [DynamoDBHashKey] //Partition key
        [DynamoDBProperty()]
        public string Name { get; set; }

        [DynamoDBRangeKey]
        public string ProjectName { get; set; }

        public string MeshUrl { get; set; }

        public string ImageUrl { get; set; }

        public string TileId { get; set; }

        public TilingInput()
        {

        }

        /// <summary>
        /// Creates Project object locally.  
        /// </summary>
        /// <param name="name">Project names in the database must be unique</param>
        protected TilingInput(string name, TilingProject project, string meshUrl, string imageUrl, string id)
        {
            Name = name;
            ProjectName = project.Name;
            MeshUrl = meshUrl;
            ImageUrl = imageUrl;
            TileId = id;
            this.IsValid();
        }


        public static TilingInput Create(DynamoDBContext context, string name, TilingProject project, string meshUrl, string imageUrl, string id)
        {
            TilingInput input = new TilingInput(name, project, meshUrl, imageUrl, id);
            context.Save(input, new DynamoDBOperationConfig() { IgnoreNullValues = true });
            return input;
        }



        public static TilingInput Find(DynamoDBContext context, TilingProject project, string name)
        {
            return context.Load<TilingInput>(name, project.Name);
        }


        public static IEnumerable<TilingInput> Find(DynamoDBContext context, Project project)
        {
            return context.Scan<TilingInput>(
                new ScanCondition("ProjectName", Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, project.Name)
                );
        }

        public void Save(DynamoDBContext context)
        {
            this.IsValid();
            context.Save(this, new DynamoDBOperationConfig() { IgnoreNullValues = true });
        }

        private void IsValid()
        {
            if (!(Name != null && ProjectName != null && MeshUrl != null))
            {
                throw new CloudException("TilingInput is missing a required field");
            }
        }
    }
}

