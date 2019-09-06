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
    public enum MeshVariant { Default = 0, Shrinkwrap = 1 }

    [DynamoDBTable("SceneMeshes")]
    [DynamoDBReadCapacity(50, 100)]
    [DynamoDBWriteCapacity(50, 100)]
    public class SceneMesh
    {
        [DynamoDBRangeKey]
        public string ProjectName;

        [DynamoDBHashKey]
        public string Name;

        public HashSet<SiteDrive> SiteDrives = new HashSet<SiteDrive>(); //immutable, empty = all site drives in project

        public String Frame;

        public MeshVariant Variant;

        public Guid MeshGuid;

        public Guid BackprojectIndexGuid;

        public Guid TextureGuid;

        protected void IsValid()
        {
            if (!(ProjectName != null && Name != null && Frame != null))
            {
                throw new Exception("missing required property in SceneMesh");
            }
        }

        //This constructor must be public for DynamoDb but should not be used
        public SceneMesh() { }

        protected SceneMesh(string projectName, string frame, SiteDrive[] siteDrives = null,
                            MeshVariant variant = MeshVariant.Default, Guid meshGuid = default(Guid),
                            Guid backprojectIndexGuid = default(Guid), Guid textureGuid = default(Guid))
        {
            this.ProjectName = projectName;
            this.Name = MakeName(frame, siteDrives, variant);
            if (siteDrives != null)
            {
                this.SiteDrives.UnionWith(siteDrives);
            }
            this.Frame = frame;
            this.Variant = variant;
            this.MeshGuid = meshGuid;
            this.BackprojectIndexGuid = backprojectIndexGuid;
            this.TextureGuid = textureGuid;
            IsValid();
        }

        public static string MakeName(string frame, SiteDrive[] siteDrives, MeshVariant variant)
        {
            string name = frame;
            if (siteDrives != null && siteDrives.Length > 0)
            {
                name += "_" + string.Join("_", siteDrives.Distinct().OrderBy(sd => sd).ToArray());
            }
            if (variant != MeshVariant.Default)
            {
                name += "_" + variant;
            }
            return name;
        }

        public static SceneMesh Create(PipelineCore pipeline, Project project, string frame,
                                       SiteDrive[] siteDrives = null, MeshVariant variant = MeshVariant.Default,
                                       Mesh mesh = null, Image backprojectIndex = null, Image texture = null)
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

            PngDataProduct textureProd = texture != null ? new PngDataProduct(texture) : null;
            if (textureProd != null)
            {
                pipeline.SaveDataProduct(project, textureProd);
            } 

            var ret = new SceneMesh(project.Name, frame, siteDrives, variant,
                                    meshProd != null ? meshProd.Guid : Guid.Empty,
                                    indexProd != null ? indexProd.Guid : Guid.Empty,
                                    textureProd != null ? textureProd.Guid : Guid.Empty);
            ret.Save(pipeline);
            return ret;
        }

        public virtual void Save(PipelineCore pipeline)
        {
            IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        public static SceneMesh Find(PipelineCore pipeline, string projectName, string frame,
                                     SiteDrive[] siteDrives = null, MeshVariant variant = MeshVariant.Default)
        {
            return pipeline.LoadDatabaseItem<SceneMesh>(MakeName(frame, siteDrives, variant), projectName);
        }

        public static IEnumerable<SceneMesh> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<SceneMesh>("ProjectName", projectName);
        }
    }
}
