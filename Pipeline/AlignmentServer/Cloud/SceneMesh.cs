using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Amazon.DynamoDBv2.DataModel;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline;

namespace OPS.Pipeline.AlignmentServer
{
    [DynamoDBTable("SceneMeshes")]
    [DynamoDBReadCapacity(50, 100)]
    [DynamoDBWriteCapacity(50, 100)]
    public class SceneMesh
    {
        [DynamoDBRangeKey]
        public string ProjectName;

        [DynamoDBHashKey]
        public string Name; //e.g. "root", "root_shrinkwrap", "SSSSSDDDDD", "SSSSSDDDDD_shrinkwrap" 

        public Guid MeshGuid;

        public Guid BackprojectIndexGuid;

        protected void IsValid()
        {
            if (!(ProjectName != null && Name != null))
            {
                throw new Exception("missing required property in SceneMesh");
            }
        }

        //This constructor must be public for DynamoDb but should not be used
        public SceneMesh() { }

        protected SceneMesh(string projectName, string name, Guid meshGuid = default(Guid),
                            Guid backprojectIndexGuid = default(Guid))
        {
            this.ProjectName = projectName;
            this.Name = name;
            this.MeshGuid = meshGuid;
            this.BackprojectIndexGuid = backprojectIndexGuid;
            IsValid();
        }

        public static SceneMesh Create(PipelineCore pipeline, Project project, string name, Mesh mesh = null,
                                       Image backprojectIndex = null)
        {
            var meshProd = mesh != null ? new PlyGZDataProduct(mesh) : null;
            if (meshProd != null)
            {
                pipeline.SaveDataProduct(project, meshProd);
            }

            TiffDataProduct indexProd = backprojectIndex != null ? new TiffDataProduct(backprojectIndex) : null;
            if (indexProd != null)
            {
                pipeline.SaveDataProduct(project, indexProd);
            } 

            var ret = new SceneMesh(project.Name, name,
                                    meshProd != null ? meshProd.Guid : Guid.Empty,
                                    indexProd != null ? indexProd.Guid : Guid.Empty);
            ret.Save(pipeline);
            return ret;
        }

        public virtual void Save(PipelineCore pipeline)
        {
            IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        public static SceneMesh Find(PipelineCore pipeline, string projectName, string name)
        {
            return pipeline.LoadDatabaseItem<SceneMesh>(name, projectName);
        }

        public static IEnumerable<SceneMesh> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<SceneMesh>("ProjectName", projectName);
        }
    }
}
