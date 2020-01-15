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

        private ConcurrentDictionary<Tuple<int, int>, UncertainRigidTransform> pdsSiteOffsets;

        public IngestPDSImage(PipelineCore pipeline, Project project, bool recreateExistingObservations = false,
                              bool resetTransforms = false, Filter filter = null,
                              ConcurrentDictionary<string, int> indices = null,
                              ConcurrentDictionary<Tuple<int, int>, UncertainRigidTransform> pdsSiteOffsets = null)
            : base(pipeline)
        {
            this.project = project;
            this.recreateExistingObservations = recreateExistingObservations;
            this.resetTransforms = resetTransforms;
            this.filter = filter;
            this.mission = MissionSpecific.GetInstance(project.Mission);
            this.pdsSiteOffsets = pdsSiteOffsets;
            
            this.indices = indices ?? new ConcurrentDictionary<string, int>();

            maxIndex = Observation.MIN_INDEX - 1;
            if (indices.Count > 0)
            {
                maxIndex = Math.Max(indices.Values.Max(), maxIndex);
            }
        }

        public override Result Ingest(string url)
        {
            try
            {
                var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                var productId = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                
                string reason = "";
                if (!mission.CheckProductId(productId, out reason)) //null ok
                {
                    pipeline.LogVerbose("rejected {0} by filename: {1}", url, reason);
                    return new Result(url, null, Status.Skipped);
                }

                if (!productId.IsSingleFrame())
                {
                    pipeline.LogVerbose("rejected multi-frame product {0}", url);
                    return new Result(url, null, Status.Skipped);
                }

                if (!productId.IsSingleCamera())
                {
                    pipeline.LogVerbose("rejected multi-camera product {0}", url);
                    return new Result(url, null, Status.Skipped);
                }

                if (!productId.IsSingleSiteDrive())
                {
                    pipeline.LogVerbose("rejected multi-sitedrive product {0}", url);
                    return new Result(url, null, Status.Skipped);
                }

                PDSMetadata metadata = null;
                try
                {
                    metadata = new PDSMetadata(pipeline.GetImageFile(url));
                }
                catch
                {
                    pipeline.LogDebug("rejected {0} by problem parsing metadata", url);
                    return new Result(url, null, Status.Skipped);
                }

                string dataUrl = null;
                if (metadata.DataPath != null)
                {
                    dataUrl = StringHelper.StripLastUrlPathSegment(url) + "/" +
                        StringHelper.NormalizeSlashes(metadata.DataPath);
                }
                else if (url.ToUpper().EndsWith(".LBL"))
                {
                    pipeline.LogDebug("rejected {0} as LBL file that does not refer to separate image data", url);
                    return new Result(url, null, Status.Skipped);
                }

                var parser = new PDSParser(metadata);
                if (!mission.CheckMetadata(parser, out reason))
                {
                    pipeline.LogVerbose("rejected {0} by metadata: {1}", url, reason);
                    return new Result(url, dataUrl, Status.Skipped);
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
                    return new Result(url, dataUrl, Status.Skipped);
                }

                if (filter != null && !filter(url, metadata, parser))
                {
                    pipeline.LogDebug("rejected {0} due to filter", observationName);
                    return new Result(url, dataUrl, Status.Skipped);
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
                    var xform = GetSiteDriveTransformFromPlaces(parser, out TransformSource source);
                    if (xform != null)
                    {
                        siteDriveFrame = GetFrame(siteDriveName, rootFrame, source, xform);
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
                    //IngestAlignmentInputs will later call FrameCache.ChainPriors()
                    var xform = PDSImage.GetSiteDriveToSiteTransformFromPDS(parser);
                    siteDriveFrame = GetFrame(siteDriveName, rootFrame, TransformSource.PDS, xform);
                }
                
                // observation (aka rover) frame -> site drive (aka local level) frame
                var observationFrameName = mission.GetObservationFrameName(parser);
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

                if (pdsSiteOffsets != null)
                {
                    if (parser.HasSiteCoordinateSystem)
                    {
                        int site = parser.Site;
                        var key = new Tuple<int, int>(site, site - 1);
                        // TODO: examine values here
                        var covariance = CreateMatrix
                            .Diagonal<double>(new double[] { 0.25, 0.25, 0.25, 0.5 * degSqr, 0.5 * degSqr, 1.0 * degSqr });
                        var xform = new UncertainRigidTransform(Matrix.CreateTranslation(parser.OffsetToPreviousSite),
                                                                covariance);
                        pdsSiteOffsets.AddOrUpdate(key, _ => xform, (_, __) => xform);
                    }
                    else if(parser.Site != 1)
                    {
                        //The SITE_COORDINATE_SYSTEM group is only set if the SITE Index is greater than 1
                        //and the Site Quaternion is not 0,0,0,0 (unknown).
                        //(from 2020 SIS, verified doesn't exist in MSL Site 1 Label)
                        pipeline.LogVerbose("PDS data product {0} missing SITE_COORDINATE_SYSTEM", url);
                    }
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
                        return new Result(url, dataUrl, Status.Duplicate, observation, observationFrame);
                    }
                }

                observation =
                    RoverObservation.Create(pipeline, observationFrame, observationName, url, cameraModel,
                                            mission.UseForAlignment(parser),
                                            mission.UseForMeshing(parser),
                                            mission.UseForTexturing(parser),
                                            metadata.Width, metadata.Height, metadata.Bands,
                                            metadata.BitDepth, mission.DayNumber(parser),
                                            productId.Version, index, parser.Site, parser.Drive,
                                            mission.GetProductType(parser), mission.GetCamera(parser),
                                            parser.ProducingInstitution, productId.Color);

                if (observation == null)
                {
                    //RoverObservation.Create() returns null if the observation already exists
                    //it shouldn't already exist, because if it did we should have just deleted or returned it
                    //but we do ingest multiple images in parallel, so there is some chance that more than one
                    //could resolve to the same observationName (which may itself be a bug)
                    //and in that case we could race here
                    pipeline.LogDebug("observation {0} already created", observationName);
                    return new Result(url, dataUrl, Status.Failed, null, observationFrame);
                }

                //don't add to frame.ObservationNames here
                //we ingest multiple images in parallel, possibly for the same frame
                //so that would be a read-modify-write hazard
                //instead this is done later in IngestAlignmentInputs
                pipeline.LogDebug("created observation {0}", observationName);
                return new Result(url, dataUrl, Status.Added, observation, observationFrame);
            }
            catch (Exception ex)
            {
                pipeline.LogError("error ingesting {0}: {1}", url, ex.Message);
                return new Result(url, null, Status.Failed);
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

        private ConcurrentDictionary<string, bool> alreadyWarned = new ConcurrentDictionary<string, bool>();

        private UncertainRigidTransform GetSiteDriveTransformFromPlaces(PDSParser parser, out TransformSource source)
        {
            source = TransformSource.PlacesDB;

            void warn(string what, Exception ex)
            {
                string msg = string.Format("failed to get PlacesDB prior for {0}: {1}", what, ex.Message);
                alreadyWarned.GetOrAdd(msg, _ => { pipeline.LogWarn(msg); return true; });
            }

            var siteDrive = new SiteDrive(parser.SiteDrive);
            Vector3 loc = Vector3.Zero;
            try
            {
                loc = Places.GetEstimatedOffsetToStart(siteDrive);
            }
            catch (Exception ex)
            {
                //if Places was not able get the offset for siteDrive it may still be able to get the site offset
                //and then we can append the offset from the PDS header to get to local_level 
                //the result is not necessarily identical to the siteDrive transform we might have gotten from Places
                //but we have seen cases where this fallback is better than nothing
                warn(string.Format("site drive {0}, trying site {1}", siteDrive, siteDrive.Site), ex);
                try
                {
                    loc = Places.GetEstimatedOffsetToStart(new SiteDrive(siteDrive.Site, 0));
                    loc += parser.OriginOffset; //local_level origin in site frame
                    source = TransformSource.PlacesDBSitePDSLocal;
                }
                catch (Exception ex2)
                {
                    warn("site " + siteDrive.Site, ex2);
                    return null;
                }
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
        /// corresponding site drive (aka local_level) frame.
        /// </summary>
        private UncertainRigidTransform GetObservationTransform(PDSParser parser)
        {
            if (!parser.RoverCoordinateSystemRelativeToSite)
            {
                throw new Exception("rover frame not relative to site frame");
            }
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
