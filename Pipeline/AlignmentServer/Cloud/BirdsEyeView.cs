using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Amazon.DynamoDBv2.DataModel;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Pipeline;

namespace OPS.Pipeline.AlignmentServer
{
    [DynamoDBTable("BirdsEyeViews")]
    [DynamoDBReadCapacity(50, 100)]
    [DynamoDBWriteCapacity(50, 100)]
    public class BirdsEyeView
    {
        [DynamoDBRangeKey]
        public string ProjectName;

        [DynamoDBHashKey]
        public string Name;

        public BirdsEyeViewing.ColorMode Coloring;

        public Meshing.BlendMode Blending;

        public double MetersPerPixel;

        public double SparseBlockSize;

        public double MinValidBlockRatio;

        public int Inpaint;

        public int Smoothing;

        public int Decimation;

        public double OriginX;

        public double OriginY;

        public int Width;

        public int Height;

        public Guid BEVGuid;

        public Guid DEMGuid;

        protected void IsValid()
        {
            if (!(ProjectName != null && Name != null &&
                  BEVGuid != null && BEVGuid != Guid.Empty &&
                  DEMGuid != null && DEMGuid != Guid.Empty))
            {
                throw new Exception("missing required property in BirdsEyeView");
            }
            if (MetersPerPixel <= 0 || SparseBlockSize < 0 || MinValidBlockRatio < 0 ||
                Inpaint < 0 || Decimation < 1 || Smoothing < 0 || Width <= 0 || Height <= 0)
            {
                throw new Exception("invalid property in BirdsEyeView");
            }
        }

        //This constructor must be public for DynamoDb but should not be used
        public BirdsEyeView() { }

        protected BirdsEyeView(string projectName, string name, Guid bevGuid, Guid demGuid, Vector2 origin,
                               int width, int height, BirdsEyeViewing.ColorMode coloring, Meshing.BlendMode blending,
                               double metersPerPixel, double sparseBlockSize, double minValidBlockRatio,
                               int inpaint, int smoothing, int decimation)
            
        {
            this.ProjectName = projectName;
            this.Name = name;
            this.Coloring = coloring;
            this.Blending = blending;
            this.MetersPerPixel = metersPerPixel;
            this.SparseBlockSize = sparseBlockSize;
            this.MinValidBlockRatio = minValidBlockRatio;
            this.Inpaint = inpaint;
            this.Smoothing = smoothing;
            this.Decimation = decimation;
            this.OriginX = origin.X;
            this.OriginY = origin.Y;
            this.Width = width;
            this.Height = height;
            this.BEVGuid = bevGuid;
            this.DEMGuid = demGuid;
            IsValid();
        }

        public static BirdsEyeView Create(PipelineCore pipeline, Project project, string name, Image bev, Image dem,
                                          Vector2 origin, BirdsEyeViewing.ColorMode coloring,
                                          Meshing.BlendMode blending, double metersPerPixel, double sparseBlockSize,
                                          double minValidBlockRatio, int inpaint, int smoothing, int decimation)
        {
            int width = bev.Width;
            int height = bev.Height;
            if (dem.Width != width || dem.Height != height)
            {
                throw new ArgumentException("DEM dimensions must match BEV");
            }
            var bevProd = new TiffDataProduct(bev);
            var demProd = new TiffDataProduct(dem);
            pipeline.SaveDataProduct(project.ProductPath, bevProd, project.Name);
            pipeline.SaveDataProduct(project.ProductPath, demProd, project.Name);
            var ret = new BirdsEyeView(project.Name, name, bevProd.Guid, demProd.Guid, origin, width, height,
                                       coloring, blending, metersPerPixel, sparseBlockSize, minValidBlockRatio,
                                       inpaint, smoothing, decimation);
            ret.Save(pipeline);
            return ret;
        }

        public static BirdsEyeView Create(PipelineCore pipeline, Frame frame, Image bev, Image dem,
                                          Vector2 origin, BirdsEyeViewing.ColorMode coloring,
                                          Meshing.BlendMode blending, double metersPerPixel, double sparseBlockSize,
                                          double minValidBlockRatio, int inpaint, int smoothing, int decimation)
        {
            return Create(pipeline, Project.Find(pipeline, frame.ProjectName), frame.Name, bev, dem,
                          origin, coloring, blending, metersPerPixel, sparseBlockSize, minValidBlockRatio,
                          inpaint, smoothing, decimation);
        }

        public static BirdsEyeView Create(PipelineCore pipeline, Project project, SiteDrive siteDrive,
                                          Image bev, Image dem, Vector2 origin, BirdsEyeViewing.ColorMode coloring,
                                          Meshing.BlendMode blending, double metersPerPixel, double sparseBlockSize,
                                          double minValidBlockRatio, int inpaint, int smoothing, int decimation)
        {
            return BirdsEyeView.Create(pipeline, project, siteDrive.ToString(), bev, dem, origin, coloring, blending,
                                       metersPerPixel, sparseBlockSize, minValidBlockRatio,
                                       inpaint, smoothing, decimation);
        }

        public virtual void Save(PipelineCore pipeline)
        {
            IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        public static BirdsEyeView Find(PipelineCore pipeline, string projectName, string name)
        {
            return pipeline.LoadDatabaseItem<BirdsEyeView>(name, projectName);
        }

        public static BirdsEyeView Find(PipelineCore pipeline, Frame frame)
        {
            return Find(pipeline, frame.ProjectName, frame.Name);
        }

        public static BirdsEyeView Find(PipelineCore pipeline, string projectName, SiteDrive siteDrive)
        {
            return Find(pipeline, projectName, siteDrive.ToString());
        }

        public static IEnumerable<BirdsEyeView> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<BirdsEyeView>("ProjectName", projectName);
        }
    }
}
