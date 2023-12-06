using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using JPLOPS.Util;
using JPLOPS.Imaging;
using JPLOPS.Geometry;

namespace JPLOPS.Pipeline.AlignmentServer
{
    public class BirdsEyeView
    {
        public enum ColorMode { Texture, Tilt, Elevation };

        public class BEVOptions : Rasterizer.Options
        {
            public WedgeObservations.CollectOptions WedgeCollectOptions;
            public WedgeObservations.MeshOptions WedgeMeshOptions;

            public int DecimateWedgeMeshes;
            public int TargetWedgeMeshResolution;
            public int DecimateWedgeImages;
            public int TargetWedgeImageResolution;

            public ColorMode Coloring;
            public bool StretchContrast;

            public string Serialize()
            {
                return JsonHelper.ToJson(this, autoTypes: false);
            }
        }

        [DBRangeKey]
        public string ProjectName;

        [DBHashKey]
        public string Name; //SSSDDDD-OptionsHASH

        [JsonIgnore]
        public SiteDrive SiteDrive
        {
            get { return new SiteDrive(Name.Substring(0, SiteDrive.StringLength)); }
        }

        public string CreationOptions; //BirdsEyeView.BEVOptions JSON

        public double MetersPerPixel; //effective meters per pixel (BEVOptions.MetersPerPixel * BEVOptions.Decimate)

        //pixel in BEV & DEM images corresponding to project root frame origin using transform priors
        public double RootOriginXPixels;
        public double RootOriginYPixels;

        [JsonIgnore]
        public Vector2 RootOriginPixel
        {
            get { return new Vector2(RootOriginXPixels, RootOriginYPixels); }
        }

        //pixel in BEV & DEM images corresponding to SiteDrive frame origin
        public double SiteDriveOriginXPixels;
        public double SiteDriveOriginYPixels;

        [JsonIgnore]
        public Vector2 SiteDriveOriginPixel
        {
            get { return new Vector2(SiteDriveOriginXPixels, SiteDriveOriginYPixels); }
        }

        //dimensions of BEV, DEM, and mask images
        public int WidthPixels;
        public int HeightPixels;

        [JsonIgnore]
        public int AreaPixels
        {
            get { return WidthPixels * HeightPixels; }
        }

        //BEV, DEM, and mask images always correspond to each other 1:1
        //always rendered in project root frame using transform priors at MetersPerPixel resolution, +X right, +Y down
        //typically, but not always, project root frame is also mission root (site 1, drive 0)
        //DEM elevations are always in meters relative to site drive origin, positive up
        //careful - mission standard coordinate frames (SITE, LOCAL_LEVEL) are +Z down
        //SiteDriveOriginPixel is the location of the site drive origin in the BEV/DEM image
        //RootOriginPixel is the location of the project root frame origin in the BEV/DEM image
        //both of those pixels may be outside the actual BEV/DEM image boundaries
        public Guid BEVGuid;
        public Guid DEMGuid;
        public Guid MaskGuid;

        private void IsValid()
        {
            if (!(ProjectName != null && Name != null &&
                  BEVGuid != null && DEMGuid != null && (BEVGuid != Guid.Empty || DEMGuid != Guid.Empty) &&
                  MaskGuid != null && MaskGuid != Guid.Empty))
            {
                throw new Exception("missing required property in BirdsEyeView");
            }
        }

        private static string MakeName(SiteDrive siteDrive, BEVOptions opts)
        {
            return string.Format("{0}-{1}", siteDrive.ToString(), StringHelper.hashHex40Char(opts.Serialize()));
        }

        public BirdsEyeView() { }

        protected BirdsEyeView(string projectName, SiteDrive siteDrive, BEVOptions opts,
                               Guid bevGuid, Guid demGuid, Guid maskGuid,
                               Vector2 rootOriginPixels, Vector2 siteDriveOriginPixels,
                               int widthPixels, int heightPixels)
        {
            ProjectName = projectName;
            Name = MakeName(siteDrive, opts);
            CreationOptions = opts.Serialize();
            MetersPerPixel = opts.MetersPerPixel * opts.Decimate;
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
                                          BEVOptions opts, Image bev, Image dem, Image mask,
                                          Vector2 rootOriginPixels, Vector2 siteDriveOriginPixels)
        {
            if (bev == null && dem == null)
            {
                throw new ArgumentException("at least one of BEV or DEM must be given");
            }

            int width = (bev ?? dem).Width;
            int height = (bev ?? dem).Height;

            if (bev != null && dem != null && (dem.Width != bev.Width || dem.Height != bev.Height))
            {
                throw new ArgumentException("DEM dimensions must match BEV");
            }

            if (mask == null || mask.Width != width || mask.Height != height)
            {
                throw new ArgumentException("mask image must be given and same size as BEV/DEM");
            }

            Guid bevGuid = Guid.Empty;
            if (bev != null)
            {
                var bevProd = new TiffDataProduct(bev);
                pipeline.SaveDataProduct(project, bevProd, noCache: true);
                bevGuid = bevProd.Guid;
            }

            Guid demGuid = Guid.Empty;
            if (dem != null)
            {
                var demProd = new TiffDataProduct(dem);
                pipeline.SaveDataProduct(project, demProd, noCache: true);
                demGuid = demProd.Guid;
            }

            var maskProd = new TiffDataProduct(mask);
            pipeline.SaveDataProduct(project, maskProd, noCache: true);
            Guid maskGuid = maskProd.Guid;

            var ret = new BirdsEyeView(project.Name, siteDrive, opts, bevGuid, demGuid, maskGuid, rootOriginPixels,
                                       siteDriveOriginPixels, width, height);
            ret.Save(pipeline);

            return ret;
        }

        public virtual void Save(PipelineCore pipeline)
        {
            IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        public static BirdsEyeView Find(PipelineCore pipeline, string projectName, SiteDrive siteDrive, BEVOptions opts)
        {
            return pipeline.LoadDatabaseItem<BirdsEyeView>(MakeName(siteDrive, opts), projectName);
        }

        public static IEnumerable<BirdsEyeView> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<BirdsEyeView>("ProjectName", projectName);
        }
    }
}
