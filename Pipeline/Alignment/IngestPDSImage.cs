using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class IngestPDSImage : IngestImage
    {
        private Project project;
        private bool recreateExistingObservations;
        private bool resetTransforms;
        private Func<PDSParser, string> observationFrameName;
        public delegate bool Filter(string imageUrl, PDSMetadata pdsMetadata, PDSParser pdsParser);
        private Filter filter;

        public MSLLocations Locations;
        public MSLPlaces Places;
        public MSLLegacyManifest LegacyManifest; 

        public IngestPDSImage(PipelineCore pipeline, Project project, Func<PDSParser, string> observationFrameName, bool recreateExistingObservations = false,
                              bool resetTransforms = false, Filter filter = null)
            : base(pipeline)
        {
            this.project = project;
            this.recreateExistingObservations = recreateExistingObservations;
            this.resetTransforms = resetTransforms;
            this.filter = filter;
            this.observationFrameName = observationFrameName;
        }

        /// <summary>
        /// Check if we should even bother reading the header, based on the filename.
        /// </summary>
        public static bool CheckFilename(string filename, bool forceLinearization = true)
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
            if (forceLinearization && id.Geometry != RoverProductGeometry.Linearized)
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
               parser.ImageSizeType == RoverProductSize.Regular &&
               !parser.IsSunFinding;
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
                if (parser.IsHazcam && parser.ExposureDuration != 0 && parser.ExposureDuration < MSLProject.MIN_NAV_HAZ_EXPOSURE)
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

            if (parser.IsHazcam)
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
                }
                catch
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
        /// Map metadata to an observation name based on product id
        /// </summary>
        /// <param name="parser"></param>
        /// <returns></returns>
        public string ObservationName(PDSParser parser)
        {
            return parser.ProductIdString;
        }
        
        private static ConcurrentDictionary<RoverProductType, ObservationType> productTypeToObservationType =
            new ConcurrentDictionary<RoverProductType, ObservationType>();

        static IngestPDSImage()
        {
            productTypeToObservationType.TryAdd(RoverProductType.Image, ObservationType.Image);
            productTypeToObservationType.TryAdd(RoverProductType.Range, ObservationType.Points);
            productTypeToObservationType.TryAdd(RoverProductType.XYZ, ObservationType.Points);
            productTypeToObservationType.TryAdd(RoverProductType.NormalMap, ObservationType.Normals);
            productTypeToObservationType.TryAdd(RoverProductType.RoverMask, ObservationType.RoverMask);
        }

        public override Result Ingest(string imgUrl)
        {
            // Parse the filename to quickly rule out data products we know we don't care about.
            if (!CheckFilename(StringHelper.GetLastUrlPathSegment(imgUrl, stripExtension: true)))
            {
                pipeline.LogDebug("rejected {0} by filename", imgUrl);
                return new Result(imgUrl, Status.Skipped);
            }

            // Fetch image and check metadata
            PDSMetadata metadata = new PDSMetadata(pipeline.GetImageFile(imgUrl));
            PDSParser parser = new PDSParser(metadata);
            if (!CheckMetadata(parser))
            {
                pipeline.LogDebug("rejected {0} by metadata", imgUrl);
                return new Result(imgUrl, Status.Skipped);
            }

            string observationName = ObservationName(parser);

            // Filter images with invalid camera models
            try
            {
                metadata.CameraModel.Unproject(new Vector2(0, 0));
            }
            catch
            {
                pipeline.LogDebug("rejected {0} for invalid camera model", observationName);
                return new Result(imgUrl, Status.Skipped);
            }

            if (filter != null && !filter(imgUrl, metadata, parser))
            {
                pipeline.LogDebug("rejected {0} due to filter", observationName);
                return new Result(imgUrl, Status.Skipped);
            }

            // Create database entries

            Frame rootFrame = Frame.Find(pipeline, project.Name, project.RootFrame);
            if (rootFrame == null)
            {
                throw new Exception(string.Format("root frame {0} does not exist", project.RootFrame));
            }

            // site drive frame -> root frame
            Frame siteDriveFrame = null;
            if (Places != null)
            {
                siteDriveFrame = GetFrame(SiteDriveFrameName(parser), rootFrame, TransformSource.PlacesDB,
                                          GetSiteDriveTransformFromPlaces(parser));
            }
            if (Locations != null)
            {
                siteDriveFrame = GetFrame(SiteDriveFrameName(parser), rootFrame, TransformSource.LocationsDB,
                                          GetSiteDriveTransformFromLocations(parser));
            }

            if (LegacyManifest != null)
            {
                var transform = GetSiteDriveTransformFromLegacyManifest(parser);
                if (transform != null)
                {
                    siteDriveFrame = GetFrame(SiteDriveFrameName(parser), rootFrame, TransformSource.LegacyManifest,
                                          transform);
                }
            }

            if (siteDriveFrame == null)
            {
                //fallback to pds headers, site relative
                siteDriveFrame = GetFrame(SiteDriveFrameName(parser), rootFrame, TransformSource.PDS,
                    GetSiteDriveTransformFromPDS(parser));
            }

            // observation (aka rover) frame -> site drive (aka local level) frame
            var observationFrame = GetFrame(observationFrameName(parser), siteDriveFrame, TransformSource.PDS,
                                            GetObservationTransform(parser));

            RoverObservation observation = RoverObservation.Find(pipeline, project.Name, observationName);
            if (observation != null)
            {
                if (recreateExistingObservations)
                {
                    pipeline.LogDebug("recreating existing observation {0}", observationName);
                    pipeline.DeleteDatabaseItem(observation);
                }
                else
                {
                    pipeline.LogDebug("not recreating existing observation {0}", observationName);
                    return new Result(imgUrl, Status.Duplicate, observation, observationFrame);
                }
            }

            observation = RoverObservation.Create(pipeline, observationFrame, observationName, imgUrl,
                                                  productTypeToObservationType[parser.DerivedImageType].ToString(),
                                                  JsonHelper.ToJson(metadata.CameraModel),
                                                  UseForReconstruction(parser, metadata),
                                                  parser.Site, parser.Drive, parser.ProductId.Version,
                                                  parser.Camera.ToString(), parser.ImageSizeType.ToString(),
                                                  parser.ProducingInstitution.ToString(),
                                                  metadata.Width, metadata.Height);
            if (observation != null)
            {
                //don't add to frame.ObservationNames here
                //we ingest multiple images in parallel, possibly for the same frame
                //so that would be a read-modify-write hazard
                //instead this is done later in IngestAlignmentInputs
                pipeline.LogDebug("created observation {0}", observationName);
                return new Result(imgUrl, Status.Added, observation, observationFrame);
            }
            else
            {
                pipeline.LogDebug("failed to create observation {0}", observationName);
                return new Result(imgUrl, Status.Failed, null, observationFrame);
            }
        }

        private double quarterDegSqr = Math.Pow(0.25 * Math.PI / 180, 2);
        private double halfDegSqr = Math.Pow(0.5 * Math.PI / 180, 2);
        private double degSqr = Math.Pow(Math.PI / 180, 2);

        /// <summary>
        /// Get transform from a site drive frame to root.  This is just the translation of the site drive frame from
        /// the MSLLocations database.
        /// </summary>
        private UncertainRigidTransform GetSiteDriveTransformFromLocations(PDSParser parser)
        {
            var siteDrive = new SiteDrive(parser.SiteDrive);

            var loc = Locations.Location(siteDrive);
            if (loc == null)
            {
                pipeline.LogWarn("no MSL location for site drive {0}", siteDrive);
            }

            if (Locations.HasBasemapDEM)
            {
                Locations.SetZFromBasemap(loc);
            }

            // TODO: examine values here
            var covariance = CreateMatrix
                .Diagonal<double>(new double[] { 8, 8, 8, 5 * degSqr, 5 * degSqr, 5 * degSqr });

            return new UncertainRigidTransform(Matrix.CreateTranslation(loc.Position), covariance);
        }

        //this function returns the site to local level offset, after all sites are processed
        // the sites can be fixed up to provide site to first site by chaining
        private UncertainRigidTransform GetSiteDriveTransformFromPDS(PDSParser parser)
        {            
            var siteDrive = new SiteDrive(parser.SiteDrive);
            Vector3 siteToLocalLevel = parser.OriginOffset;

            // TODO: examine values here
            var covariance = CreateMatrix
                .Diagonal<double>(new double[] { 0.25, 0.25, 0.25, 0.5 * degSqr, 0.5 * degSqr, 1.0 * degSqr });

            return new UncertainRigidTransform(Matrix.CreateTranslation(siteToLocalLevel), covariance);
        }

        private UncertainRigidTransform GetSiteDriveTransformFromPlaces(PDSParser parser)
        {
            var siteDrive = new SiteDrive(parser.SiteDrive);
            var loc = Places.GetEstimatedOffsetFromStart(siteDrive);
            if (loc == null)
            {
                pipeline.LogWarn("no MSL Places for site drive {0}", siteDrive);
            }

            // TODO: examine values here
            var covariance = CreateMatrix
                .Diagonal<double>(new double[] { 0.25, 0.25, 0.25, 0.5 * degSqr, 0.5 * degSqr, 1.0 * degSqr });

            return new UncertainRigidTransform(Matrix.CreateTranslation(loc), covariance);
        }

        private UncertainRigidTransform GetSiteDriveTransformFromLegacyManifest(PDSParser parser)
        {
            var siteDrive = new SiteDrive(parser.SiteDrive);

            Matrix? mat = LegacyManifest.GetRelativeTransformPrimaryToSiteDrive(siteDrive);
            if (!mat.HasValue)
            {
                pipeline.LogWarn("no MSL legacy manifest for site drive {0}", siteDrive);
                return null;
            }
            else
            {
                Matrix siteDriveToPrimarySiteDrive = Matrix.Invert(mat.Value);
                // TODO: examine values here
                var covariance = CreateMatrix
                    .Diagonal<double>(new double[] { 0.25, 0.25, 0.25, 0.5 * degSqr, 0.5 * degSqr, 1.0 * degSqr });

                return new UncertainRigidTransform(siteDriveToPrimarySiteDrive, covariance);
            }
        }
        /// <summary>
        /// Get transform from observation frame, which is rover frame at the time the observation was acquired, to the
        /// corresponding site drive (aka local level) frame.
        /// </summary>
        private UncertainRigidTransform GetObservationTransform(PDSParser parser)
        {
            // TODO: examine values here
            var covariance = CreateMatrix
                .Diagonal<double>(new double[] { 0.01, 0.01, 0.01, quarterDegSqr, quarterDegSqr, halfDegSqr });
            return new UncertainRigidTransform(RoverCoordinateSystem.RoverToLocalLevel(parser.RoverOriginRotation),
                                               covariance);
        }

        private ConcurrentDictionary<string, bool> alreadyResetTransforms = new ConcurrentDictionary<string, bool>();

        private Frame GetFrame(string name, Frame parent, TransformSource source, UncertainRigidTransform transform)
        {
            var frame = Frame.FindOrCreate(pipeline, project.Name, name, parent);
            var frameTransform = FrameTransform.Find(pipeline, frame, source);
            if (frameTransform == null)
            {
                pipeline.LogDebug("creating {0} transform for frame {1}", source, name);
                frameTransform = FrameTransform.Create(pipeline, frame, source, transform);
                //don't add to frame.Transforms here
                //we ingest multiple images in parallel, possibly for the same frame
                //so that would be a read-modify-write hazard
                //instead this is done later in IngestAlignmentInputs
            }
            else if (resetTransforms && !alreadyResetTransforms.ContainsKey(name))
            {
                pipeline.LogDebug("resetting {0} transform for frame {1}", source, name);
                frameTransform.Transform = transform;
                frameTransform.Save(pipeline);
                alreadyResetTransforms.TryAdd(name, true);
            }
            return frame;
        }
    }
}
