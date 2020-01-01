using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Amazon.DynamoDBv2.DataModel;
using Newtonsoft.Json;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline;

namespace OPS.Pipeline.AlignmentServer
{
    [DynamoDBTable("BirdsEyeViews")]
    [DynamoDBReadCapacity(50, 100)]
    [DynamoDBWriteCapacity(50, 100)]
    public class BirdsEyeView
    {
        public enum ColorMode { Texture, Tilt, Elevation };

        public class BEVOptions : Rasterizer.BEVOptions
        {
            public int DecimateWedgeMeshes; //Wedge mesh decimation blocksize, 0 to disable, -1 for auto
            public int TargetWedgeMeshResolution; //used when DecimateWedgeMeshes = -1 (auto)
            public int DecimateWedgeImages; //Wedge image decimation blocksize, 0 to disable, -1 for auto
            public int TargetWedgeImageResolution; //used when DecimateWedgeImages = -1 (auto)
            public ColorMode Coloring;
        }

        [DynamoDBRangeKey]
        public string ProjectName;

        [DynamoDBHashKey]
        public string Name; //sitedrive in form SSSDDDD

        [DynamoDBIgnore]
        [JsonIgnore]
        public SiteDrive SiteDrive
        {
            get { return new SiteDrive(Name); }
        }

        public long CreationTime; //UTC ms since epoch when this BEV was created

        public string CreationOptions; //BirdsEyeView.BEVOptions JSON

        public double MetersPerPixel; //effective meters per pixel (BEVOptions.MetersPerPixel * BEVOptions.Decimate)

        //pixel in BEV & DEM images corresponding to project root frame origin using transform priors
        public double RootOriginXPixels;
        public double RootOriginYPixels;

        [DynamoDBIgnore]
        [JsonIgnore]
        public Vector2 RootOriginPixel
        {
            get { return new Vector2(RootOriginXPixels, RootOriginYPixels); }
        }

        //pixel in BEV & DEM images corresponding to SiteDrive frame origin
        public double SiteDriveOriginXPixels;
        public double SiteDriveOriginYPixels;

        [DynamoDBIgnore]
        [JsonIgnore]
        public Vector2 SiteDriveOriginPixel
        {
            get { return new Vector2(SiteDriveOriginXPixels, SiteDriveOriginYPixels); }
        }

        //dimensions of BEV, DEM, and mask images
        public int WidthPixels;
        public int HeightPixels;

        [DynamoDBIgnore]
        [JsonIgnore]
        public int AreaPixels
        {
            get { return WidthPixels * HeightPixels; }
        }

        //BEV, DEM, and mask images always correspond to each other 1:1
        //they are always rendered using transform priors at the resolution given by MetersPerPixel
        //use SiteDriveOriginPixel for site-drive relative alignment, or if a site-drive transform is known
        //use RootOriginPixel for absolute alignment using transform priors
        //typically project root frame is also mission root (site 1, drive 0) which enables orbital alignment
        public Guid BEVGuid;
        public Guid DEMGuid;
        public Guid MaskGuid;

        protected void IsValid()
        {
            if (!(ProjectName != null && Name != null &&
                  BEVGuid != null && BEVGuid != Guid.Empty &&
                  DEMGuid != null && DEMGuid != Guid.Empty &&
                  MaskGuid != null && MaskGuid != Guid.Empty))
            {
                throw new Exception("missing required property in BirdsEyeView");
            }
        }

        //This constructor must be public for DynamoDb but should not be used
        public BirdsEyeView() { }

        protected BirdsEyeView(string projectName, SiteDrive siteDrive, BEVOptions bevOptions,
                               Guid bevGuid, Guid demGuid, Guid maskGuid,
                               Vector2 rootOriginPixels, Vector2 siteDriveOriginPixels,
                               int widthPixels, int heightPixels)
        {
            ProjectName = projectName;
            Name = siteDrive.ToString();
            CreationTime = (long)UTCTime.NowMS();
            CreationOptions = JsonHelper.ToJson(bevOptions);
            MetersPerPixel = bevOptions.MetersPerPixel * bevOptions.Decimate;
            RootOriginXPixels = rootOriginPixels.X;
            RootOriginYPixels = rootOriginPixels.Y;
            SiteDriveOriginXPixels= siteDriveOriginPixels.X;
            SiteDriveOriginYPixels = siteDriveOriginPixels.Y;
            WidthPixels = widthPixels;
            HeightPixels = heightPixels;
            BEVGuid = bevGuid;
            DEMGuid = demGuid;
            MaskGuid = maskGuid;
            IsValid();
        }

        public static BirdsEyeView Create(PipelineCore pipeline, Project project, SiteDrive siteDrive,
                                          BEVOptions bevOptions, Image bev, Image dem, Image mask,
                                          Vector2 rootOriginPixels, Vector2 siteDriveOriginPixels)
        {
            int width = bev.Width;
            int height = bev.Height;
            if (dem.Width != width || dem.Height != height || mask.Width != width || mask.Height != height)
            {
                throw new ArgumentException("DEM and mask dimensions must match BEV");
            }
            var bevProd = new TiffDataProduct(bev);
            var demProd = new TiffDataProduct(dem);
            var maskProd = new PngDataProduct(mask);
            pipeline.SaveDataProduct(project, bevProd);
            pipeline.SaveDataProduct(project, demProd);
            pipeline.SaveDataProduct(project, maskProd);
            var ret = new BirdsEyeView(project.Name, siteDrive, bevOptions, bevProd.Guid, demProd.Guid, maskProd.Guid,
                                       rootOriginPixels, siteDriveOriginPixels, width, height);
            ret.Save(pipeline);
            return ret;
        }

        public virtual void Save(PipelineCore pipeline)
        {
            IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        public static BirdsEyeView Find(PipelineCore pipeline, string projectName, SiteDrive siteDrive)
        {
            return Find(pipeline, projectName, siteDrive.ToString());
        }

        public static BirdsEyeView Find(PipelineCore pipeline, string projectName, string siteDrive)
        {
            return pipeline.LoadDatabaseItem<BirdsEyeView>(siteDrive, projectName);
        }

        public static IEnumerable<BirdsEyeView> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<BirdsEyeView>("ProjectName", projectName);
        }
    }
}
