using MathNet.Numerics.LinearAlgebra;
using Microsoft.Xna.Framework;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Plumbing;
using OPS.Util;
using OPS.Pipeline.AlignmentServer;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace OPS.Pipeline
{
    public class IngestPDSImage : IngestImage
    {
        public readonly string projectName;
        private MSLLocations locations;

        public IngestPDSImage(PipelineCore pipeline, MSLLocations locations, string projectName) : base(pipeline)
        {
            this.locations = locations;
            this.projectName = projectName;
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
                // Filter for color or black and white jpegs that are not thumbnails
                if(msssId.MSSSProductType == MSSSProductType.Unknown)
                {
                    return false;
                }
            }
            
            //ISSUE #353: need to validate that alignment works across cameras with non-linearized images.
            // so not allowing non-aligned images to be used when other aligned images are being used.
            if(id.Geometry != RoverProductGeometry.Linearized)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Mostly just confirms what CheckFilename did using metadata instead of the filename
        /// </summary>
        /// <param name="parser"></param>
        /// <returns></returns>
        bool CheckMetadata(PDSParser parser)
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

            //Needed for mask computation
            try
            {
                if (parser.Articulation == null)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            if(parser.IsHazcam)
            {
                return false;
            }

            // Only use single and 3 band images
            if (metadata.Bands != 3 && metadata.Bands != 1)
            {
                return false;
            }
            if (parser.IsMastcam)
            {
                // Skip mastcam taken with color filters
                try
                {
                    if (!parser.FilterNumber.HasValue || parser.FilterNumber != 0)
                    {
                        return false;
                    }
                } catch
                {
                    return false;
                }

                // Skip mastcam with short focal distances (probably closeup of rover part with terrain out of focus in background)
                if (parser.MaximumFocusDistance.HasValue && parser.MaximumFocusDistance < MSLProject.MIN_MASTCAM_FOCUS_CUTOFF)
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
            productTypeToObservationType.TryAdd(RoverProductType.NormalMap, ObservationType.Normals);
            productTypeToObservationType.TryAdd(RoverProductType.RoverMask, ObservationType.RoverMask);
        }

        private double quarterDegSqr = Math.Pow(0.25 * Math.PI / 180, 2);
        private double halfDegSqr = Math.Pow(0.5 * Math.PI / 180, 2);
        private double degSqr = Math.Pow(Math.PI / 180, 2);

        public override Result Ingest(ImageRef imgRef)
        {
            // Parse the filename to quickly rule out data products we know we don't care about.
            if (!CheckFilename(imgRef.DisplayName))
            {
                return new Result(Status.Skipped, null);
            }

            // Fetch image and check metadata
            PDSMetadata metadata = null;
            Pipeline.GetStream(imgRef, stream => { metadata = new PDSMetadata(stream); });
            
            if (metadata == null)
            {
                return new Result(Status.Failed, null);
            }

            PDSParser parser = new PDSParser(metadata);
            if (!CheckMetadata(parser))
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

            // Create database entries
            Project project = Project.Find(Pipeline, projectName);
            if (project == null)
            {
                throw new CloudException("Project does not exist");
            }

            // Create frames for this observation if necessary
            Frame rootFrame = Frame.Find(Pipeline, project.Name, MSLProject.ROOT_FRAME_NAME);
            if (rootFrame == null)
            {
                throw new Exception("Root frame does not exist");
            }
            Frame siteDriveFrame = Frame.FindOrCreate(Pipeline, project, SiteDriveFrameName(parser), rootFrame);
            Frame observationFrame = Frame.FindOrCreate(Pipeline, project, ObservationFrameName(parser), siteDriveFrame);

            if (FrameTransform.Find(Pipeline, observationFrame) == null)
            {
                // TODO: examine values here
                var covariance = CreateMatrix.Diagonal<double>(new double[] { 0.01, 0.01, 0.01, quarterDegSqr, quarterDegSqr, halfDegSqr });

                // Create a transform that goes from observation frame (aka rover) to site drive frame (aka local level)
                Quaternion roverToLocalLevel = parser.RoverOriginRotation;
                UncertainRigidTransform observationToSiteDriveTransform = new UncertainRigidTransform(Matrix.CreateFromQuaternion(roverToLocalLevel), covariance);
                FrameTransform observationToSiteDrive = FrameTransform.Create(Pipeline, observationFrame, observationToSiteDriveTransform);

                TransformPrior o2sdP = TransformPrior.Create(Pipeline, observationFrame, observationToSiteDriveTransform);
                observationFrame.PriorIds.Add(o2sdP.Id);
                observationFrame.Save(Pipeline);
            }
            // Create a transform that goes from site drive frame to root frame

            var loc = locations.Location(new SiteDrive(parser.SiteDrive));
            if (loc == null)
            {
                throw new Exception("site drive transform does not exist");
            }

            if (FrameTransform.Find(Pipeline, siteDriveFrame) == null)
            {
                // TODO: examine values here
                var covariance = CreateMatrix.Diagonal<double>(new double[] { 8, 8, 8, 5 * degSqr, 5 * degSqr, 5 * degSqr });
                UncertainRigidTransform transform = new UncertainRigidTransform(Matrix.CreateTranslation(loc.Position), covariance);
                FrameTransform siteDriveToRoot = FrameTransform.Create(Pipeline, siteDriveFrame, transform);
                TransformPrior sd2rP = TransformPrior.Create(Pipeline, siteDriveFrame, transform);
                siteDriveFrame.PriorIds.Add(sd2rP.Id);
                siteDriveFrame.Save(Pipeline);
            }

            string observationName = ObservationName(parser);
            Observation observation = RoverObservation.Find(Pipeline, project.Name, observationName);
            if (observation == null)
            {
                string cameraModel = JsonHelper.ToJson(metadata.CameraModel);
                string url = imgRef.Url;
                observation = RoverObservation.Create(Pipeline, observationFrame, observationName, url, productTypeToObservationType[parser.DerivedImageType].ToString(), cameraModel, UseForReconstruction(parser, metadata), parser.Site, parser.Drive, parser.ProductId.Version, parser.Camera.ToString(), parser.ImageSizeType.ToString(), parser.ProducingInstitution.ToString(), metadata.Width, metadata.Height);
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
