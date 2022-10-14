using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using JPLOPS.Imaging;

namespace JPLOPS.Pipeline.AlignmentServer
{
    public class SceneHeightmap
    {
        [DBRangeKey]
        public string ProjectName;

        [DBHashKey]
        public string Name;

        public double MetersPerPixel;

        public double OriginX;

        public double OriginY;

        public int Width;

        public int Height;

        public Guid DEMGuid;

        protected void IsValid()
        {
            if (!(ProjectName != null && Name != null &&
                  DEMGuid != null && DEMGuid != Guid.Empty))
            {
                throw new Exception("missing required property in SceneHeightmap");
            }
            if (MetersPerPixel <= 0 ||  Width <= 0 || Height <= 0)
            {
                throw new Exception("invalid property in SceneHeightmap");
            }
        }

        public SceneHeightmap() { }

        protected SceneHeightmap(string projectName, string name, Guid demGuid,
                               Vector2 origin, int width, int height, double metersPerPixel)

        {
            this.ProjectName = projectName;
            this.Name = name;
            this.MetersPerPixel = metersPerPixel;
            this.OriginX = origin.X;
            this.OriginY = origin.Y;
            this.Width = width;
            this.Height = height;
            this.DEMGuid = demGuid;
            IsValid();
        }

        public static SceneHeightmap Create(PipelineCore pipeline, Project project, string name,
                                Image dem, Vector2 origin, double metersPerPixel)
        {
            var demProd = new TiffDataProduct(dem);
            pipeline.SaveDataProduct(project, demProd, noCache: true);
            var ret = new SceneHeightmap(project.Name, name, demProd.Guid, origin,
                                       dem.Width, dem.Height, metersPerPixel);
            ret.Save(pipeline);
            return ret;
        }

        public virtual void Save(PipelineCore pipeline)
        {
            IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        public static SceneHeightmap Find(PipelineCore pipeline, string projectName, string name)
        {
            return pipeline.LoadDatabaseItem<SceneHeightmap>(name, projectName);
        }

        public static IEnumerable<SceneHeightmap> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<SceneHeightmap>("ProjectName", projectName);
        }
    }
}
