using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Amazon.DynamoDBv2.DataModel;
using OPS.Util;
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

        public HashSet<string> Observations = new HashSet<string>(); //immutable, empty = all site drives in project

        public String Frame;

        public MeshVariant Variant;

        public string Bounds;

        public double SurfaceExtent; //-1 if unlimited, 0 if no surface (only orbital)

        public Guid MeshGuid;

        public Guid BackprojectIndexGuid;

        public Guid TextureGuid;

        public Guid BlurredTextureGuid;

        public Guid BlendedTextureGuid;

        public Guid TileListGuid;

        protected void IsValid()
        {
            if (!(ProjectName != null && Name != null && Frame != null))
            {
                throw new Exception("missing required property in SceneMesh");
            }
        }

        //This constructor must be public for DynamoDb but should not be used
        public SceneMesh() { }

        protected SceneMesh(string projectName, string frame, MeshVariant variant = MeshVariant.Default,
                            SiteDrive[] siteDrives = null, string[] observations = null,
                            Guid meshGuid = default(Guid), Guid textureGuid = default(Guid),
                            double surfaceExtent = -1)
        {
            this.ProjectName = projectName;
            this.Name = MakeName(frame, variant, siteDrives, observations);
            if (siteDrives != null)
            {
                this.SiteDrives.UnionWith(siteDrives);
            }
            if (observations != null)
            {
                this.Observations.UnionWith(observations);
            }
            this.Frame = frame;
            this.Variant = variant;
            this.MeshGuid = meshGuid;
            this.TextureGuid = textureGuid;
            this.BackprojectIndexGuid = Guid.Empty;
            this.BlurredTextureGuid = Guid.Empty;
            this.BlendedTextureGuid = Guid.Empty;
            this.TileListGuid = Guid.Empty;
            this.SurfaceExtent = surfaceExtent;
            IsValid();
        }

        public static string MakeName(string frame, MeshVariant variant, SiteDrive[] siteDrives, string[] observations)
        {
            string name = frame;
            if (variant != MeshVariant.Default)
            {
                name += "_" + variant;
            }
            if (siteDrives != null && siteDrives.Length > 0)
            {
                name += "_" + string.Join("_", siteDrives.Distinct().OrderBy(sd => sd).ToArray());
            }
            if (observations != null && observations.Length > 0)
            {
                name += "_" + string.Join("_", observations.Distinct().OrderBy(obs => obs).ToArray());
            }
            return name;
        }

        public static SceneMesh Create(PipelineCore pipeline, Project project, string frame,
                                       MeshVariant variant = MeshVariant.Default,
                                       SiteDrive[] siteDrives = null, string[] observations = null,
                                       Mesh mesh = null, Image texture = null, double surfaceExtent = -1,
                                       bool noSave = false)
        {
            var meshProd = mesh != null ? new PlyGZDataProduct(mesh) : null;
            if (meshProd != null)
            {
                pipeline.SaveDataProduct(project, meshProd);
            }

            PngDataProduct textureProd = texture != null ? new PngDataProduct(texture) : null;
            if (textureProd != null)
            {
                pipeline.SaveDataProduct(project, textureProd);
            } 

            var ret = new SceneMesh(project.Name, frame, variant, siteDrives, observations,
                                    meshProd != null ? meshProd.Guid : Guid.Empty,
                                    textureProd != null ? textureProd.Guid : Guid.Empty,
                                    surfaceExtent);

            if (mesh != null)
            {
                ret.SetBounds(mesh.Bounds());
            }

            bool addedToProject = false;
            lock (project.SceneMeshes)
            {
                addedToProject = project.SceneMeshes.Add(ret.Name);
            }

            if (!noSave)
            {
                ret.Save(pipeline);

                if (addedToProject)
                {
                    project.Save(pipeline);
                }
            }

            return ret;
        }

        public BoundingBox? GetBounds()
        {
            if (!string.IsNullOrEmpty(Bounds))
            {
                return (BoundingBox)JsonHelper.FromJson(Bounds);
            }
            return null;
        }

        public void SetBounds(BoundingBox bounds)
        {
            Bounds = JsonHelper.ToJson(bounds);
        }

        public void Delete(PipelineCore pipeline, Project project, bool ignoreErrors = true)
        {
            bool removedFromProject = false;
            lock (project.SceneMeshes)
            {
                removedFromProject = project.SceneMeshes.Remove(Name);
            }
            if (removedFromProject)
            {
                project.Save(pipeline);
            }

            pipeline.DeleteDatabaseItem(this, ignoreErrors);
        }

        public virtual void Save(PipelineCore pipeline)
        {
            IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        public static SceneMesh Load(PipelineCore pipeline, string projectName, string name)
        {
            return pipeline.LoadDatabaseItem<SceneMesh>(name, projectName);
        }

        public static SceneMesh Find(PipelineCore pipeline, string projectName, string frame,
                                     MeshVariant variant = MeshVariant.Default,
                                     SiteDrive[] siteDrives = null, string[] observations = null)
        {
            return pipeline.LoadDatabaseItem<SceneMesh>(MakeName(frame, variant, siteDrives, observations), projectName);
        }

        public static IEnumerable<SceneMesh> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<SceneMesh>("ProjectName", projectName);
        }
    }
}
