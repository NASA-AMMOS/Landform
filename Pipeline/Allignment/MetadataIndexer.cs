using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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

using Amazon.DynamoDBv2.DataModel;

namespace OPS.Pipeline
{
    public class MSLProject
    {
        public const string PROJECT_NAME = "MSL";
        public const string ROOT_FRAME_NAME = "root";

        //constants for cutoffs
        public const int MIN_NAV_HAZ_EXPOSURE = 80;
        public const int MIN_MASTCAM_FOCUS_CUTOFF = 3;
        public const int MAX_MASTCAM_WIDTH = 1344;
    }

    public enum Status { ADDED, FAILEDTOADD, PREEXISTING, SKIPPED};

    /// <summary>
    /// Return type for indexer so caller can decide what action to take with the message that started this job
    /// </summary>
    public class MetadataIndexerStatus
    {
        public Status status;
        public Observation obs;
    }

    /// <summary>
    /// Downloads images, reads metadata, and saves metadata to DynamoDB
    /// Threadsafe. 
    /// </summary>
    public class MetadataIndexer
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(MetadataIndexer));

        //Thread-safe shared helpers
        private DynamoDBContext context;
        private StorageHelper storage; //TODO I think this is thread safe, but it's not documented as such
        private MSLLocations locations;

        static ConcurrentDictionary<RoverProductType, ObservationType> productTypeToObservationType = new ConcurrentDictionary<RoverProductType, ObservationType>();
        static MetadataIndexer()
        {
            productTypeToObservationType.TryAdd(RoverProductType.Image, ObservationType.Image);
            productTypeToObservationType.TryAdd(RoverProductType.Range, ObservationType.Points);
            productTypeToObservationType.TryAdd(RoverProductType.XYZ, ObservationType.Points);
        }

        public MetadataIndexer(DynamoDBContext context, StorageHelper storage)
        {
            this.context = context;
            this.storage = storage;
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
        /// Return an observation IFF it is in the database. Else (if should not index or error) return null. 
        /// </summary>
        /// <param name="storage"></param>
        /// <param name="url"></param>
        public MetadataIndexerStatus IndexMetadata(string url)
        {
            MetadataIndexerStatus toReturn = new MetadataIndexerStatus();

            //Rule out some files
            if (!ShouldDownloadHeader(url))
            {
                toReturn.status = Status.SKIPPED;
                return toReturn;
            }

            //Index the rest! 
            storage.GetStorageStream(url, stream =>
            {
                string status = "";
                    PDSMetadata metadata = new PDSMetadata(stream);
                    PDSParser parser = new PDSParser(metadata);
                    if (ShouldIndexBasedOnMetadata(parser))
                    {
                        Console.WriteLine("My observation name was " + ObservationName(parser));

                        Project project = Project.Find(context, MSLProject.PROJECT_NAME);
                    if (project == null) throw new CloudException("Project does not exist");
                        SiteDrive sd = new SiteDrive(parser.Site, parser.Drive);

                        Frame siteDriveFrame = Frame.FindOrCreate(context, project, SiteDriveFrameName(parser));
                        Frame observationFrame = Frame.FindOrCreate(context, project, ObservationFrameName(parser));
                        Frame rootFrame = Frame.Find(context, project, MSLProject.ROOT_FRAME_NAME);
                        Quaternion roverToLocalLevel = parser.RoverOriginRotation;

                        //TODO Charley said we'll save multiple transforms per frame/frame pair but right now we specifically don't
                        if (FrameTransform.Find(context, observationFrame, siteDriveFrame).FirstOrDefault() == null)
                        {
                            FrameTransform observationToSiteDrive = FrameTransform.Create(context, observationFrame, siteDriveFrame, Vector3.Zero, roverToLocalLevel, TransformSource.Prior, 0);
                        }
                        var loc = locations.Location(sd);
                        if (loc != null && FrameTransform.Find(context, siteDriveFrame, rootFrame).FirstOrDefault() == null)
                        {
                            FrameTransform siteDriveToRoot = FrameTransform.Create(context, siteDriveFrame, rootFrame, loc.Position, Quaternion.Identity, TransformSource.Prior, 0.5);
                        }

                        string observationName = ObservationName(parser);
                        Observation observation = RoverObservation.Find(context, project.Name, observationName);
                        if (observation == null)
                        {
                            string cameraModel = JsonHelper.ToJson(metadata.CameraModel);
                            observation = RoverObservation.Create(context, observationFrame, observationName, url, productTypeToObservationType[parser.DerivedImageType].ToString(), cameraModel, UseForReconstruction(parser, metadata), parser.Site, parser.Drive, parser.ProductId.Version, parser.Camera.ToString(), parser.ImageSizeType.ToString());
                            //observation = Observation.Create(Context, observationFrame, observationName, url, productTypeToObservationType[parser.DerivedImageType].ToString(), cameraModel, UseForReconstruction(parser, metadata));
                            if (observation != null)
                            {
                                status = "Add";
                                toReturn.obs = observation;
                                toReturn.status = Status.ADDED;
                            }
                            else
                            {
                                status = "Failed to add";
                                toReturn.status = Status.FAILEDTOADD;
                            }
                        }
                        else
                        {
                            status = "Exists";
                            toReturn.obs = observation;
                            toReturn.status = Status.PREEXISTING;
                        }

                    }
                    else
                    {
                        status = "Skipped(metadata)";
                        toReturn.status = Status.SKIPPED;
                    }
                Console.WriteLine(url + "\t" + status);
            });
            return toReturn;
        }      
    }
}
