using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.Imaging;
using JPLOPS.Geometry;

namespace JPLOPS.Pipeline.AlignmentServer
{
    public enum MeshVariant { Default = 0, Shrinkwrap = 1, Sky = 2 }

    public class SceneMesh
    {
        [DBRangeKey]
        public string ProjectName;

        [DBHashKey]
        public string Name;

        public MeshVariant Variant;

        public string Bounds;

        public double SurfaceExtent; //-1 if unlimited, 0 if no surface (only orbital)

        public Guid MeshGuid;

        public Guid TextureGuid;

        public Guid BackprojectIndexGuid;

        public Guid StretchedTextureGuid;

        public Guid BlurredTextureGuid;

        public Guid BlendedTextureGuid;

        public Guid TileListGuid;

        public Guid TextureProjectorGuid;

        protected void IsValid()
        {
            if (!(ProjectName != null && Name != null))
            {
                throw new Exception("missing required property in SceneMesh");
            }
        }

        public SceneMesh() { }

        protected SceneMesh(string projectName, MeshVariant variant = MeshVariant.Default,
                            Guid meshGuid = default(Guid), Guid textureGuid = default(Guid),
                            double surfaceExtent = -1)
        {
            this.ProjectName = projectName;
            this.Name = variant.ToString();
            this.Variant = variant;
            this.MeshGuid = meshGuid;
            this.TextureGuid = textureGuid;
            this.SurfaceExtent = surfaceExtent;
            IsValid();
        }

        public static SceneMesh Create(PipelineCore pipeline, Project project,
                                       MeshVariant variant = MeshVariant.Default,
                                       Mesh mesh = null, Image texture = null, double surfaceExtent = -1,
                                       bool noSave = false)
        {
            var meshProd = mesh != null ? new PlyGZDataProduct(mesh) : null;
            if (meshProd != null && !noSave)
            {
                pipeline.SaveDataProduct(project, meshProd, noCache: true);
            }

            PngDataProduct textureProd = texture != null ? new PngDataProduct(texture) : null;
            if (textureProd != null && !noSave)
            {
                pipeline.SaveDataProduct(project, textureProd, noCache: true);
            } 

            var ret = new SceneMesh(project.Name, variant, meshProd != null ? meshProd.Guid : Guid.Empty,
                                    textureProd != null ? textureProd.Guid : Guid.Empty, surfaceExtent);

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

        public static SceneMesh Find(PipelineCore pipeline, string projectName, MeshVariant variant)
        {
            return pipeline.LoadDatabaseItem<SceneMesh>(variant.ToString(), projectName);
        }

        public static IEnumerable<SceneMesh> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<SceneMesh>("ProjectName", projectName);
        }
    }
}
