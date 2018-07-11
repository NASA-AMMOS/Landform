using MathNet.Numerics.LinearAlgebra;
using Microsoft.Xna.Framework;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Plumbing;
using OPS.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{

    public class MSLProject
    {
        public const string ROOT_FRAME_NAME = "root";

        //constants for cutoffs
        public const int MIN_NAV_HAZ_EXPOSURE = 80;
        public const int MIN_MASTCAM_FOCUS_CUTOFF = 3;
        public const int MAX_MASTCAM_WIDTH = 1344;
    }

    public class IngestPDSImage : IngestImage
    {
        public readonly string project_name;
        MSLLocations locations;

        public IngestPDSImage(PipelineCore output_pipeline, string project_name = "MSL") : base(output_pipeline)
        {
            locations = new MSLLocations();
            this.project_name = project_name;
        }

        /// <summary>
        /// Check if we should even bother reading the header, based on the filename.
        /// </summary>
        public static bool CheckFilename(string filename)
        {
            
            RoverProductId id = RoverProductId.ParseFromString(filename);
            if (id == null)
            {
                return false;
            }
            if (id.Camera == RoverProductCamera.Unknown)
            {
                return false;
            }
            if (id.ProductType == RoverProductType.Unknown)
            {
                return false;
            }
            if (id.Producer == RoverProductProducer.OPGS)
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
            if (parser.DerivedImageType == RoverProductType.Image)
            {
                if (parser.ExposureDuration != 0 && parser.ExposureDuration < MSLProject.MIN_NAV_HAZ_EXPOSURE)
                {
                    return false;
                }
            }
            if (parser.IsMastcam)
            {
                // Skip single band mastcams
                if (metadata.Bands != 3)
                {
                    return false;
                }
                // Skip mastcam taken with color filters
                try
                {
                    if (parser.FilterNumber != 0)
                    {
                        return false;
                    }
                } catch
                {
                    return false;
                }

                // Skip mastcam with short focal distances (probably closeup of rover part with terrain out of focus in background)
                if (parser.MaximumFocusDistance < MSLProject.MIN_MASTCAM_FOCUS_CUTOFF)
                {
                    return false;
                }
                // Assume that if the mastcam is big enough to cause vignetting on the ccd that this has been special processed
                // We do mask the vignetted parts so this check may not be strictly neccessary and may reduce our available images
                // unneccessarily in some cases
                if (metadata.Width > MSLProject.MAX_MASTCAM_WIDTH)
                {
                    return false;
                }
            }
            if (parser.IsNavcam && parser.IsDownsampled)
            {
                return false;
            }
            return true;
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
        
        static ConcurrentDictionary<RoverProductType, ObservationType> productTypeToObservationType = new ConcurrentDictionary<RoverProductType, ObservationType>();
        static IngestPDSImage()
        {
            productTypeToObservationType.TryAdd(RoverProductType.Image, ObservationType.Image);
            productTypeToObservationType.TryAdd(RoverProductType.Range, ObservationType.Points);
            productTypeToObservationType.TryAdd(RoverProductType.XYZ, ObservationType.Points);
        }

        public override Result Ingest(S3ImageRef imgRef)
        {
            if (imgRef is ObservationImageRef)
            {
                throw new InvalidOperationException("hey now, let's not get *too* weird");
            }

            // Parse the filename to quickly rule out data products we know we don't care about.
            if (!CheckFilename(imgRef.DisplayName))
            {
                return new Result(Status.Skipped, null);
            }

            // Fetch image and check metadata
            Image img = GetImage(imgRef);
            PDSMetadata metadata = img.Metadata as PDSMetadata;
            if (metadata == null)
            {
                return new Result(Status.Failed, null);
            }

            PDSParser parser = new PDSParser(metadata);
            if (!ShouldIndexBasedOnMetadata(parser))
            {
                return new Result(Status.Skipped, null);
            }

            // Filter images with invalid camera models
            try
            {
                metadata.CameraModel.Unproject(new Vector2(0, 0));
            }
            catch
            {
                return new Result(Status.Skipped, null);
            }

            bool useForReconstruction = UseForReconstruction(parser, metadata);

            // Create database entries
            Project project = Project.Find(DynamoDB, project_name);
            if (project == null)
            {
                throw new CloudException("Project does not exist");
            }

            // Create frames for this observation if necessary
            Frame rootFrame = Frame.Find(DynamoDB, project.Name, MSLProject.ROOT_FRAME_NAME);
            Frame siteDriveFrame = Frame.FindOrCreate(DynamoDB, project, SiteDriveFrameName(parser), rootFrame);
            Frame observationFrame = Frame.FindOrCreate(DynamoDB, project, ObservationFrameName(parser), siteDriveFrame);

            if (FrameTransform.Find(DynamoDB, observationFrame) == null)
            {
                // TODO: examine values here
                double quarterDegSqr = Math.Pow(0.25 * Math.PI / 180, 2);
                double halfDegSqr = Math.Pow(0.5 * Math.PI / 180, 2);
                var covariance = CreateMatrix.Diagonal<double>(new double[] { 0.01, 0.01, 0.01, quarterDegSqr, quarterDegSqr, halfDegSqr });

                // Create a transform that goes from observation frame (aka rover) to site drive frame (aka local level)
                Quaternion roverToLocalLevel = parser.RoverOriginRotation;
                UncertainRigidTransform observationToSiteDriveTransform = new UncertainRigidTransform(Matrix.CreateFromQuaternion(roverToLocalLevel), covariance);
                FrameTransform observationToSiteDrive = FrameTransform.Create(DynamoDB, observationFrame, observationToSiteDriveTransform, TransformSource.Prior);

                TransformPrior o2sdP = TransformPrior.Create(DynamoDB, observationFrame, observationToSiteDriveTransform);
                observationFrame.PriorIds.Add(o2sdP.Id);
                observationFrame.Save(DynamoDB);
            }
            // Create a transform that goes from site drive frame to root frame

            var loc = locations.Location(new SiteDrive(parser.SiteDrive));
            if (loc != null && FrameTransform.Find(DynamoDB, siteDriveFrame) == null)
            {
                // TODO: examine values here
                double halfDegSqr = Math.Pow(0.5 * Math.PI / 180, 2);
                double degSqr = Math.Pow(1.0 * Math.PI / 180, 2);
                var covariance = CreateMatrix.Diagonal<double>(new double[] { 0.25, 0.25, 0.25, halfDegSqr, halfDegSqr, degSqr });
                UncertainRigidTransform transform = new UncertainRigidTransform(Matrix.CreateTranslation(loc.Position), covariance);
                FrameTransform siteDriveToRoot = FrameTransform.Create(DynamoDB, siteDriveFrame, transform, TransformSource.Prior);
                TransformPrior sd2rP = TransformPrior.Create(DynamoDB, siteDriveFrame, transform);
                siteDriveFrame.PriorIds.Add(sd2rP.Id);
                siteDriveFrame.Save(DynamoDB);
            }

            string observationName = ObservationName(parser);
            Observation observation = RoverObservation.Find(DynamoDB, project.Name, observationName);
            if (observation == null)
            {
                string cameraModel = JsonHelper.ToJson(metadata.CameraModel);
                string url = imgRef.Url;
                observation = RoverObservation.Create(DynamoDB, observationFrame, observationName, url, productTypeToObservationType[parser.DerivedImageType].ToString(), cameraModel, UseForReconstruction(parser, metadata), parser.Site, parser.Drive, parser.ProductId.Version, parser.Camera.ToString(), parser.ImageSizeType.ToString(), metadata.Width, metadata.Height);
                if (observation != null) {
                    return new Result(Status.Added, observation);
                }
                else
                {
                    return new Result(Status.Failed, null);
                }
            }
            else
            {
                return new Result(Status.Duplicate, observation);
            }
        }
    }
}
