using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
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
        public MSLLocations Locations;
        public MSLPlaces Places;
        public MSLLegacyManifest LegacyManifest; 

        private Project project;

        private bool recreateExistingObservations;
        private bool resetTransforms;

        private MissionSpecific mission;

        public delegate bool Filter(string imageUrl, PDSMetadata pdsMetadata, PDSParser pdsParser);
        private Filter filter;

        private ConcurrentDictionary<string, int> indices;
        private int maxIndex;

        public IngestPDSImage(PipelineCore pipeline, Project project, bool recreateExistingObservations = false,
                              bool resetTransforms = false, Filter filter = null,
                              ConcurrentDictionary<string, int> indices = null)
            : base(pipeline)
        {
            this.project = project;
            this.recreateExistingObservations = recreateExistingObservations;
            this.resetTransforms = resetTransforms;
            this.filter = filter;
            this.mission = MissionSpecific.GetInstance(project.Mission);

            this.indices = indices ?? new ConcurrentDictionary<string, int>();

            maxIndex = Observation.MIN_INDEX - 1;
            if (indices.Count > 0)
            {
                maxIndex = Math.Max(indices.Values.Max(), maxIndex);
            }
        }

        public override Result Ingest(string imgUrl)
        {
            try
            {
                var filename = StringHelper.GetLastUrlPathSegment(imgUrl, stripExtension: true);
                
                // Parse the filename to quickly rule out data products we know we don't care about.
                if (!mission.CheckFilename(filename))
                {
                    pipeline.LogDebug("rejected {0} by filename", imgUrl);
                    return new Result(imgUrl, Status.Skipped);
                }

                PDSMetadata metadata = null;
                try
                {
                    metadata = new PDSMetadata(pipeline.GetImageFile(imgUrl));
                }
                catch
                {
                    pipeline.LogDebug("rejected {0} by problem parsing metadata", imgUrl);
                    return new Result(imgUrl, Status.Skipped);
                }

                var parser = new PDSParser(metadata);
                if (!mission.CheckMetadata(parser))
                {
                    pipeline.LogDebug("rejected {0} by metadata", imgUrl);
                    return new Result(imgUrl, Status.Skipped);
                }
                
                var observationName = parser.ProductIdString;
                var siteDriveName = parser.SiteDrive;

                if (metadata.CameraModel.Linear != mission.IsGeometricallyLinearlyCorrected(parser))
                {
                    var cmName = metadata.CameraModel.GetType().Name;
                    pipeline.LogWarn("PDS header geometry {0} but camera model {1} for {2}, using {1}",
                                     parser.GeometricProjection, cmName, parser.ProductIdString);
                }
                var cameraModel = metadata.CameraModel;

                // Filter images with invalid camera models
                try
                {
                    cameraModel.Unproject(new Vector2(0, 0));
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
                string rootName = mission.RootFrameName();
                Frame rootFrame = Frame.Find(pipeline, project.Name, rootName);
                if (rootFrame == null)
                {
                    throw new Exception(string.Format("root frame {0} does not exist", rootName));
                }
                
                // site drive frame -> root frame
                Frame siteDriveFrame = null;

                if (Places != null)
                {
                    var xform = GetSiteDriveTransformFromPlaces(parser);
                    if (xform != null)
                    {
                        siteDriveFrame = GetFrame(siteDriveName, rootFrame, TransformSource.PlacesDB, xform);
                    }
                }

                if (Locations != null)
                {
                    var xform = GetSiteDriveTransformFromLocations(parser);
                    if (xform != null)
                    {
                        siteDriveFrame = GetFrame(siteDriveName, rootFrame, TransformSource.LocationsDB, xform);
                    }
                }
                
                if (LegacyManifest != null)
                {
                    var xform = GetSiteDriveTransformFromLegacyManifest(parser);
                    if (xform != null)
                    {
                        siteDriveFrame = GetFrame(siteDriveName, rootFrame, TransformSource.LegacyManifest, xform);
                    }
                }
                
                if (siteDriveFrame == null)
                {
                    //fallback to pds headers, site relative
                    var xform = GetSiteDriveTransformFromPDS(parser);
                    siteDriveFrame = GetFrame(siteDriveName, rootFrame, TransformSource.PDS, xform);
                }
                
                // observation (aka rover) frame -> site drive (aka local level) frame
                var cameraName = mission.CameraName(parser);
                var observationFrameName = cameraName + "_" + mission.RoverMotionCounter(parser);
                var observationFrame = GetFrame(observationFrameName, siteDriveFrame,
                                                TransformSource.PDS, GetObservationTransform(parser));

                //if we already assigned an index to this observation, re-use it
                //that way already created backproject index images have a fighting chance of not being stale
                //otherwise assign the next available index
                int index = indices.GetOrAdd(observationName, _ => Interlocked.Increment(ref maxIndex));

                if (index < Observation.MIN_INDEX)
                {
                    //the main case where we could get here is when re-ingesting a project with existing observations
                    //that were created prior to adding the Index field
                    //in that case the existing index will be 0 which is less than MIN_INDEX
                    index = Interlocked.Increment(ref maxIndex);
                    indices.AddOrUpdate(observationName, _ => index, (_, __) => index);
                }

                var observation = RoverObservation.Find(pipeline, project.Name, observationName);
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
                        if (observation.Index != index)
                        {
                            pipeline.LogDebug("updating index {0} -> {1} on existing observation {2}",
                                              observation.Index, index, observationName);
                            observation.Index = index;
                            observation.Save(pipeline);
                        }
                        return new Result(imgUrl, Status.Duplicate, observation, observationFrame);
                    }
                }

                var obsType = Observation.ProductTypeToObservationType(parser.DerivedImageType).ToString();

                observation = RoverObservation.Create(pipeline, observationFrame, observationName, imgUrl, obsType,
                                                      JsonHelper.ToJson(cameraModel),
                                                      mission.UseForReconstruction(parser),
                                                      parser.Site, parser.Drive, parser.ProductId.Version,
                                                      cameraName, parser.ImageSizeType.ToString(),
                                                      parser.ProducingInstitution.ToString(),
                                                      metadata.Width, metadata.Height, metadata.Bands,
                                                      metadata.BitDepth, mission.DayNumber(parser), index);

                if (observation == null)
                {
                    //RoverObservation.Create() returns null if the observation already exists
                    //it shouldn't already exist, because if it did we should have just deleted or returned it
                    //but we do ingest multiple images in parallel, so there is some chance that more than one
                    //could resolve to the same observationName (which may itself be a bug)
                    //and in that case we could race here
                    pipeline.LogDebug("observation {0} already created", observationName);
                    return new Result(imgUrl, Status.Failed, null, observationFrame);
                }

                //don't add to frame.ObservationNames here
                //we ingest multiple images in parallel, possibly for the same frame
                //so that would be a read-modify-write hazard
                //instead this is done later in IngestAlignmentInputs
                pipeline.LogDebug("created observation {0}", observationName);
                return new Result(imgUrl, Status.Added, observation, observationFrame);
            }
            catch (MetadataException ex)
            {
                pipeline.LogError("error parsing metadata for {0}: {1}", imgUrl, ex.Message);
                return new Result(imgUrl, Status.Failed);
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
                return null;
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

        //this function returns local level to site
        //TODO: after all sites are processed the matrices can be fixed up to provide current site to root (first site) by chaining
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
            if(!Places.GetEstimatedOffsetFromStart(siteDrive, out Vector3 loc))
            {
                pipeline.LogWarn("no MSL Places for site drive {0}", siteDrive);
                return null;
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
