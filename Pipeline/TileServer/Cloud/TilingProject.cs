using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using Amazon.DynamoDBv2.DataModel;
using OPS.Geometry;

namespace OPS.Pipeline.TileServer
{

    [DynamoDBTable("TilingProjects")]
    [DynamoDBReadCapacity(5, 50)]
    [DynamoDBWriteCapacity(5, 50)]
    public class TilingProject
    {
        [DynamoDBHashKey] //Partition key
        [DynamoDBProperty()]
        public string Name { get; set; }

        public string TilingScheme { get; set; }
        public string SkirtMode { get; set; }

        public int FacesPerTile { get; set; }
        public int TileResolution { get; set; }

        public bool TilesDefined { get; set; }

        public TilingProject()
        {

        }

        /// <summary>
        /// Creates Project object locally.  
        /// </summary>
        /// <param name="name">Project names in the database must be unique</param>
        protected TilingProject(string name, TilingScheme tilingScheme, SkirtMode skirtMode, int faces, int resolution)
        {
            Name = name;
            TilingScheme = tilingScheme.ToString();
            SkirtMode = skirtMode.ToString();
            FacesPerTile = faces;
            TileResolution = resolution;
            TilesDefined = false;
            this.IsValid();
        }


        public static TilingProject Create(DynamoDBContext context, string name, TilingScheme tilingScheme, SkirtMode skirtMode, int faces, int resolution)
        {
            TilingProject project = new TilingProject(name, tilingScheme, skirtMode, faces, resolution);
            context.Save(project, new DynamoDBOperationConfig() { IgnoreNullValues = true });
            return project;
        }

        public static TilingProject Find(DynamoDBContext context, string name)
        {
            TilingProject project = context.Load<TilingProject>(name);
            if (project != null)
            {
                project.IsValid();
            }
            return project;
        }

        public void Save(DynamoDBContext context)
        {
            this.IsValid();
            context.Save(this, new DynamoDBOperationConfig() { IgnoreNullValues = true });
        }

        private void IsValid()
        {
            if (!(Name != null && TilingScheme != null && SkirtMode != null))
            {
                throw new CloudException("TilingProject is missing a required field");
            }
        }

        public TilingScheme GetTilingScheme()
        {
            return (TilingScheme)Enum.Parse(typeof(TilingScheme), this.TilingScheme, true);
        }

        public SkirtMode GetSkirtMode()
        {
            return (SkirtMode)Enum.Parse(typeof(SkirtMode), this.SkirtMode, true);
        }
    }
}
