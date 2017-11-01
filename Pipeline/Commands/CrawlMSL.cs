using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using CommandLine;
using OPS.Imaging;
using OPS.Util;
using System.Threading;
using System.IO;
using log4net;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{

    [Verb("crawlmsl", HelpText = "Crawl MSL S3 bucket for dataproducts and add them to the landform database")]
    public class CralwMSLOptions
    {
        [Option(Required = true, HelpText = "Name of the aws profile to use to authenticate with s3")]
        public string AwsProfile { get; set; }

        [Option(Required = true, HelpText = "Starting sol on s3 to index")]

        public int StartSol { get; set; }

        [Option(Required = true, HelpText = "Ending sol on s3 to index")]

        public int EndSol { get; set; }
    }

    public class MSLProject
    {
        public const string PROJECT_NAME = "MSL";
        public const string ROOT_FRAME_NAME = "root";

        //constants for cutoffs
        public const int MIN_NAV_HAZ_EXPOSURE = 80;
        public const int MIN_MASTCAM_FOCUS_CUTOFF = 3;
        public const int MAX_MASTCAM_WIDTH = 1344;

    }

    /// <summary>
    /// The crawl MSL command 
    /// </summary>
    public class CrawlMSL
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(CrawlMSL));


        CralwMSLOptions options;
        LandformDatabase database;
        MSLLocations locations;

        static Dictionary<RoverProductType, ObservationType> productTypeToObservationType = new Dictionary<RoverProductType, ObservationType>();
        static CrawlMSL()
        {
            productTypeToObservationType.Add(RoverProductType.Image, ObservationType.Image);
            productTypeToObservationType.Add(RoverProductType.Range, ObservationType.Points);
            productTypeToObservationType.Add(RoverProductType.XYZ, ObservationType.Points);
        }

        public CrawlMSL(CralwMSLOptions options)
        {
            this.options = options;
            this.database = new LandformDatabase();
            this.locations = new MSLLocations();
        }                
                
        /// <summary>
        /// Map metadata to a frame name based on site drive
        /// </summary>
        /// <param name="parser"></param>
        /// <returns></returns>
        public string SiteDriveFrameName(PDSParser parser)
        {
            return parser.SiteDrive;
        }

        /// <summary>
        /// Map metadata to an observation frame name based on RMC
        /// </summary>
        /// <param name="parser"></param>
        /// <returns></returns>
        public string ObservationFrameName(PDSParser parser)
        {
            return parser.Camera.ToString() + "_" + parser.RMC;
        }

        /// <summary>
        /// Map metadata to an observation name based on product id
        /// </summary>
        /// <param name="parser"></param>
        /// <returns></returns>
        public string ObservationName(PDSParser parser)
        {
            return parser.ProductIdString;
        }

        bool ShouldIndexDirectory(string folder)
        {
            return true;
        }

        /// <summary>
        /// Decide if this is a file we should index from what can be dervied from the filename
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        bool ShouldDownloadHeader(string url)
        {
            string filename = Path.GetFileName(url);
            RoverProductId id = RoverProductId.ParseFromString(filename);
            if(id == null)
            {
                return false;
            }
            if(id.Camera == RoverProductCamera.Unknown)
            {
                return false;
            }
            if(id.ProductType == RoverProductType.Unknown)
            {
                return false;
            }
            if(id.Producer == RoverProductProducer.OPGS)
            {
                OPGSProductId opgsId = (OPGSProductId)id;
                if (opgsId.Size != RoverProductSize.Regular)
                { 
                    return false;
                }
            }
            if (id.Producer == RoverProductProducer.MSSS)
            {
                // Check that this is a DCX file
                MSSSProductId msssId = (MSSSProductId)id;
                if (!msssId.RadiometricallyCalibrated || !msssId.ColorCorrected || !msssId.Decompressed)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Mostly just confirms what ShouldDownloadHeader did using metadata instead of the filename
        /// </summary>
        /// <param name="parser"></param>
        /// <returns></returns>
        bool ShouldIndexBasedOnMetadata(PDSParser parser)
        {
            return productTypeToObservationType.ContainsKey(parser.DerivedImageType) &&
                    parser.ImageSizeType == RoverProductSize.Regular;                    
        }

        /// <summary>
        /// Return true if this file should be used for reconstruction
        /// </summary>
        /// <param name="parser"></param>
        /// <param name="metadata"></param>
        /// <returns></returns>
        bool UseForReconstruction(PDSParser parser, PDSMetadata metadata)
        {
            // Partial downloads
            if (parser.IsPartial)
            {
                return false;
            }
            // Low exposure hazcams
            if(parser.DerivedImageType == RoverProductType.Image)
            {
                if(parser.ExposureDuration != 0 && parser.ExposureDuration < MSLProject.MIN_NAV_HAZ_EXPOSURE)
                {
                    return false;
                }
            }
            if(parser.IsMastcam)
            {
                // Skip single band mastcams
                if (metadata.Bands != 3)
                {
                    return false;
                }
                // Skip mastcam taken with color filters
                if (parser.FilterNumber != 0)
                {
                    return false;
                }
                // Skip mastcam with short focal distances (probably closeup of rover part with terrain out of focus in background)
                if (parser.MaximumFocusDistance < MSLProject.MIN_MASTCAM_FOCUS_CUTOFF)
                {
                    return false;
                }
                // Assume that if the mastcam is bigger enough to cause vinetting on the ccd that this has been special processed
                // We do mask the vinetted parts so this check may not be strictly neccessary and may reduce our available images
                // unneccessarily in some cases
                if (metadata.Width > MSLProject.MAX_MASTCAM_WIDTH)
                {
                    return false;
                }
            }
            if(parser.IsNavcam && parser.IsDownsampled)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Add a file to the database
        /// </summary>
        /// <param name="storage"></param>
        /// <param name="url"></param>
        void IndexMetadata(StorageHelper storage, string url)
        {
            storage.GetStorageStream(url, stream =>
            {
                string status = "";
                try
                {
                    PDSMetadata metadata = new PDSMetadata(stream);
                    PDSParser parser = new PDSParser(metadata);
                    if (ShouldIndexBasedOnMetadata(parser))
                    {
                        using (LandformDbContext context = database.CreateContext())
                        {
                            Project project = Project.Find(context, MSLProject.PROJECT_NAME);
                            SiteDrive sd = new SiteDrive(parser.Site, parser.Drive);
                            
                            Frame siteDriveFrame = Frame.FindOrCreate(context, project, SiteDriveFrameName(parser));
                            Frame observationFrame = Frame.FindOrCreate(context, project, ObservationFrameName(parser));
                            Frame rootFrame = Frame.Find(context, project, MSLProject.ROOT_FRAME_NAME);
                            Quaternion roverToLocalLevel = parser.RoverOriginRotation;
                            if(FrameTransform.Find(context, observationFrame, siteDriveFrame).FirstOrDefault() == null)
                            {
                                FrameTransform observationToSiteDrive = FrameTransform.Create(context, observationFrame, siteDriveFrame, Vector3.Zero, roverToLocalLevel, TransformSource.Prior, 0);
                            }
                            var loc = locations.Location(sd);
                            if (loc != null && FrameTransform.Find(context, siteDriveFrame, rootFrame).FirstOrDefault() == null)
                            {
                                FrameTransform siteDriveToRoot = FrameTransform.Create(context, siteDriveFrame, rootFrame, loc.Position, Quaternion.Identity, TransformSource.Prior, 0.5);
                            }                            
                            string observationName = ObservationName(parser);
                            Observation observation = RoverObservation.Find(context, project, observationName);
                            if (observation == null)
                            {
                                string cameraModel = JsonHelper.ToJson(metadata.CameraModel);
                                observation = RoverObservation.Create(context, observationFrame, observationName, url, productTypeToObservationType[parser.DerivedImageType].ToString(), cameraModel, UseForReconstruction(parser, metadata), parser.Site, parser.Drive, parser.ProductId.Version, parser.Camera.ToString(), parser.ImageSizeType.ToString());
                                if (observation != null)
                                {
                                    status = "Add";
                                }
                                else
                                {
                                    status = "Failed to add";
                                }
                            }
                            else
                            {
                                status = "Exists";
                            }
                        }
                    }
                    else
                    {
                        status = "Skipped(metadata)";
                    }
      
                }
                catch (Exception e)
                {
                    status = "Failed " + e.Message;
                }
                logger.Info(url + "\t" + status);
            });
        }
        
        /// <summary>
        /// Add files in a directory to the database
        /// </summary>
        /// <param name="storage"></param>
        /// <param name="dir"></param>
        void IndexDirectory(StorageHelper storage, string dir)
        {
            foreach (var url in storage.SearchObjects(dir, "*.IMG", true))
            {
                if (ShouldDownloadHeader(url))
                {
                    IndexMetadata(storage, url);
                }
            }
        }

        public int Run()
        {
            using (LandformDbContext context = database.CreateContext())
            {
                Project p = Project.FindOrCreate(context, MSLProject.PROJECT_NAME);
                Frame.FindOrCreate(context, p, MSLProject.ROOT_FRAME_NAME);
            }
            string opgsPattern = "s3://red-product/proj/msl/redops/ods/surface/sol/{0}/opgs/rdr";
            string msssPattern = "s3://red-product/ods/surface/sol/{0}/soas/rdr";
            StorageHelper storage = new StorageHelper(options.AwsProfile);
            Parallel.For(options.StartSol, options.EndSol+1, sol => 
            {
                
                foreach (string pattern in new string[] { opgsPattern, msssPattern })
                {
                    string dir = string.Format(pattern, sol.ToString().PadLeft(5, '0'));
                    if (ShouldIndexDirectory(dir))
                    {
                        IndexDirectory(storage, dir);
                    }
                }
            });            
            return 0;
        }       
    }
}
