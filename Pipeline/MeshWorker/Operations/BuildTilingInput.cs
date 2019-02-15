using System;
using System.Collections.Generic;
using System.Linq;
using log4net;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline.TileServer;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline.MeshWorker
{
    public class BuildTilingInputMessage : QueueMessage
    {
        public BuildTilingInputMessage() { }
        public BuildTilingInputMessage(string projectName) : base(projectName) { }
    }

    /// <summary>
    /// create a large mesh from input data and uploads it as the tiling input
    /// </summary>
    public class BuildTilingInput : CloudPipelineOperation
    {
        private readonly BuildTilingInputMessage message;

        public BuildTilingInput(CloudPipeline pipeline, BuildTilingInputMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        struct PointCloudObservations
        {
            public RoverObservation PointsObs;
            public RoverObservation NormalObs;
        }

        struct PointCloudImage
        {
            public RoverObservation Obs;
            public Image PDSImage;
            public Image GeneratedImage;
        }

        struct PointCloudInput
        {
            public PointCloudImage Points;
            public PointCloudImage Normals;
            public PointCloudImage RoverMask;
            public PointCloudImage Confidence;
        }

        public int Process()
        {
            LogInfo("started");

            //cache data needed to build pointcloud
            FrameCache frameCache = new FrameCache(pipeline, projectName);
            ObservationCache obsCache = new ObservationCache(pipeline, projectName);
            obsCache.Preload(obs => obs.UseForReconstruction);

            //find the best observations to use for each point cloud
            List<PointCloudObservations> pointCloudObservations = CollectPointCloudInputs(obsCache, frameCache);
            if (pointCloudObservations.Count == 0)
            {
                LogError("no observations were found to build a point cloud");
                return 1;
            }
            
            //accumulate the large point cloud
            Mesh aggregatePointCloud = new Mesh(hasNormals: true);
            for (int idx = 0; idx < pointCloudObservations.Count; idx++)
            {
                LogInfo("building point cloud {0}/{1} ({2})%): {3}",
                        idx+1, pointCloudObservations.Count,
                        (int)(100 * idx / (float)pointCloudObservations.Count),
                        pointCloudObservations[idx].PointsObs.FrameName);

                PointCloudInput? pcImgs = GetPointCloudInput(pointCloudObservations[idx]);
                if( pcImgs == null)
                {
                    LogWarn("Failed to get pointcloud input for " + pointCloudObservations[idx].PointsObs.FrameName);
                    continue;
                }

                Mesh pointCloud = BuildPointCloudMesh(pcImgs.Value, frameCache, obsCache);
                if (pointCloud != null)
                {
                    aggregatePointCloud.MergeWith(new Mesh[] { pointCloud }, false);
                }
            }
            
            // build the large mesh from the aggregate point cloud using poisson reconstruction
            if (aggregatePointCloud.Vertices.Count == 0)
            {
                LogError("aggregate point cloud contains no points");
                return 1;
            }
          
            LogInfo("reconstructing point cloud: " + aggregatePointCloud.Vertices.Count() + " vertices");
            PoissonReconstruction.Options opts = new PoissonReconstruction.Options
            {
                Boundary = PoissonReconstruction.BoundaryTypes.Dirichlet,   // suppresses the large wings often seen when extrapolating without orbital data 
                MinOctreeCellWidthMeters = 0.05f,                           // no features should be finer than this many meters as this is the finest the octree will dice
                MinOctreeSamplesPerCell = 15,                               // a value on the upper end of the suggested range in the docs meaning we think our data in noisy, so wait for this many samples in a cell
                BSplineDegree = 2,                                          // attempts to allow higher order surfaces than the defaults
                UseNormalsForConfidence = true                              // indicates the normal magnitudes are not uniformly unit scaled to indicate confidence in the position attached to it
            };

            Mesh surfacedMesh = PoissonReconstruction.Reconstruct(aggregatePointCloud, opts);            
            if (surfacedMesh == null || surfacedMesh.Vertices.Count == 0)
            {
                LogError("point cloud failed to reconstruct");
                return 1;
            }

            //upload mesh
            string meshName = "FullMesh";
            string meshOutputUrl = pipeline.GetStorageUrl("input", projectName, meshName + ".ply");
            TemporaryFile.GetAndDelete(".ply", tempFile =>
            {
                LogInfo("uploading mesh " + meshOutputUrl);
                surfacedMesh.Save(tempFile);
                pipeline.SaveFile(tempFile, meshOutputUrl);
            });

            //create a tiling input
            TilingProject tilingProject = TilingProject.Find(pipeline, projectName);
            TilingInput.Create(pipeline, meshName, tilingProject, meshOutputUrl, null, null);
            
            //indicate successs to the tiling server master
            pipeline.MasterQueue.Enqueue(new BuildTilingInputMessage(projectName));

            LogInfo("complete");

            return 0;
        }

        /// <summary>
        /// collects the imagery needed to build the pointclouds: point cloud data, normals, and confidence and then 
        /// transforms them into a processed image in the format later stages expect
        /// </summary>       
        /// <returns>
        ///  a pointcloudinput containing:
        ///     a link to the original observation
        ///     the pds mission product for the observation (or one with consistent metadata)
        ///     a generated product: the many varieties of source data formats transformed into a single expected format with a validity mask
        /// </returns>
        private PointCloudInput? GetPointCloudInput(PointCloudObservations pointCloudObservations)
        {
            PointCloudInput pct = new PointCloudInput();

            // build the point cloud input, generated result is an XYR (3d points in rover frame) with a mask marking invalid pixels
            pct.Points.Obs = pointCloudObservations.PointsObs;
            pct.Points.PDSImage = GetObservationImage(pointCloudObservations.PointsObs, RoverProductType.Range);
            pct.Points.GeneratedImage = ConvertRNGToXYR(pct.Points.PDSImage);

            if(pct.Points.GeneratedImage == null)
            {
                LogWarn("failed to generate XYR data");
                return null;
            }

            pct.Normals.Obs = pointCloudObservations.NormalObs;
            pct.Normals.PDSImage = GetObservationImage(pointCloudObservations.NormalObs, RoverProductType.NormalMap);
            pct.Normals.GeneratedImage = GenerateNormalsImage(pct.Normals.PDSImage);

            pct.RoverMask.Obs = null;
            pct.RoverMask.PDSImage = pct.Points.PDSImage; //Issue #259 use metadata from points image, until real mask product is available
            pct.RoverMask.GeneratedImage = GenerateRoverMask(pct.RoverMask.PDSImage);

            pct.Confidence.Obs = null;
            pct.Confidence.PDSImage = pct.Points.PDSImage; //Issue #259 use metadata from points image, until real error product is available
            pct.Confidence.GeneratedImage = GenerateConfidenceImage(pct.Confidence.PDSImage);

            if ((pct.Points.GeneratedImage.Width != pct.Normals.GeneratedImage.Width) ||
                (pct.Points.GeneratedImage.Height != pct.Normals.GeneratedImage.Height))
            {
                LogWarn("mismatched resolutions across points and normals");
                return null;
            }

            return pct;
        }

        private Image GetObservationImage(RoverObservation obs, params RoverProductType[] expectedProductTypes)
        {
            Image img = pipeline.LoadImage(obs.Url);

            //don't mutate cached image
            img = (Image)img.Clone();

            PDSParser parser = new PDSParser((PDSMetadata)img.Metadata);

            if (parser.ProductId.Producer != RoverProductProducer.OPGS)
                throw new NotImplementedException("unexpected producer for image");

            OPGSProductId opgsId = (OPGSProductId)parser.ProductId;
            if (!expectedProductTypes.Contains(opgsId.ProductType))
                throw new NotImplementedException("image for observation is not a known product type");

            img.CreateMask(parser.MissingConstant.Select(x => (float)x).ToArray());

            return img;
        }

        /// <summary>
        /// the format of the generated normals product is consistent to the UVW mission product
        ///     but normals within a pixel of an invalid area are ignored to avoid an issue
        ///     seen where normals close to invalid areas frequently face downwards
        /// </summary>
        private Image GenerateNormalsImage(Image pdsImage)
        {
            Image normals = new Image(pdsImage);
            for (int idxRow = 0; idxRow < pdsImage.Metadata.Height; idxRow++)
            {
                for (int idxCol = 0; idxCol < pdsImage.Metadata.Width; idxCol++)
                {
                    if (pdsImage.IsInvalid(idxRow, idxCol))
                        continue;

                    int up = Math.Max(0, idxRow - 1);
                    int down = Math.Min(idxRow + 1, pdsImage.Height - 1);
                    int left = Math.Max(0, idxCol - 1);
                    int right = Math.Min(idxCol + 1, pdsImage.Width - 1);

                    if (pdsImage.IsInvalid(up, left) ||
                        pdsImage.IsInvalid(up, idxCol) ||
                        pdsImage.IsInvalid(up, right) ||
                        pdsImage.IsInvalid(idxRow, left) ||
                        pdsImage.IsInvalid(idxRow, right) ||
                        pdsImage.IsInvalid(down, left) ||
                        pdsImage.IsInvalid(down, idxCol) ||
                        pdsImage.IsInvalid(down, right))
                    {
                        normals.SetMaskValue(idxRow, idxCol, true);
                    }
                }
            }

            return normals;
        }

        /// <summary>
        /// until the rover mask product is available in S3, we g
        /// generate our own by raycasting an articulated mesh.
        /// Mask is the inversion (0: not occluded by rover 1: occluded) of the rovermask (0: occluded, 1: not occluded)
        /// </summary>
        private Image GenerateRoverMask(Image pdsImage)
        {
            Image roverMask = RoverMask.Build(pdsImage);
            roverMask.CreateMask(new float[] { 0.0f });
            return roverMask;
        }

        /// <summary>
        /// until mission products giving useful error estimates are available
        /// this code generates a confidence that is inversely proportional to range
        /// </summary>
        private Image GenerateConfidenceImage(Image pdsImage)
        {
            PDSParser parser = new PDSParser((PDSMetadata)pdsImage.Metadata);
            if (parser.ProductId.Producer != RoverProductProducer.OPGS)
                throw new NotImplementedException("unexpected producer for points image");

            OPGSProductId opgsId = (OPGSProductId)parser.ProductId;
            if (opgsId.ProductType != RoverProductType.Range)
                throw new NotImplementedException("synthetic confidence supported from range images currently"); ;

            Image confidence = new Image(1, pdsImage.Metadata.Width, pdsImage.Metadata.Height);
            confidence.CreateMask(false);

            for (int idxRow = 0; idxRow < pdsImage.Metadata.Height; idxRow++)
            {
                for (int idxCol = 0; idxCol < pdsImage.Metadata.Width; idxCol++)
                {
                    if (pdsImage.IsInvalid(idxRow, idxCol) || pdsImage[0, idxRow, idxCol] <= 0.0f)
                    {
                        confidence.SetMaskValue(idxRow, idxCol, true);
                    }
                    else
                    {
                        //naive confidence: farther away the point is, the lower the confidence
                        confidence[0, idxRow, idxCol] = 1 / pdsImage[0, idxRow, idxCol];
                    }
                }
            }

            return pdsImage;
        }

        /// <summary>
        /// this converts the range products into an image similar to the XYR products
        /// a position in the rover frame, until mission XYZ/XYR products are available
        /// </summary>
        public Image ConvertRNGToXYR(Image img)
        {
            //validate assumptions about input data for rng images
            PDSParser rangePDR = new PDSParser((PDSMetadata)img.Metadata);
            if (rangePDR.DerivedImageRefFrame != PDSParser.ReferenceCoordinateFrame.RoverNav)
            {
                if (rangePDR.CameraModelRefFrame != PDSParser.ReferenceCoordinateFrame.RoverNav)
                {
                    LogWarn("non-rover frame camera model not supported yet");
                    return null;
                }

                CAHV cahv = img.CameraModel as CAHV;
                if (cahv == null)
                {
                    LogWarn("only cahv, cahvor, cahvore camera models handled currently");
                    return null;
                }

                // find the range data's origin in rover frame
                Vector3 cameraPosRover = Vector3.Transform(rangePDR.RangeOrigin, RoverCoordinateSystem.SiteToRover(rangePDR.RoverOriginRotation, rangePDR.OriginOffset));

                //verify we can use the camera model's position as the origin for the range data
                if (!Vector3.AlmostEqual(cameraPosRover, cahv.C, 0.0005))
                {
                    LogWarn("only expecting range maps from the camera's location");
                    return null;
                }
            }

            Image xyr = new Image(3, img.Metadata.Width, img.Metadata.Height);
            xyr.CreateMask(false);

            for (int idxRow = 0; idxRow < img.Metadata.Height; idxRow++)
            {
                for (int idxCol = 0; idxCol < img.Metadata.Width; idxCol++)
                {
                    if (img.IsInvalid(idxRow, idxCol))
                    {
                        xyr.SetMaskValue(idxRow, idxCol, true);
                    }
                    else
                    {
                        Vector3 pt = img.CameraModel.Unproject(new Vector2(idxCol, idxRow), img[0, idxRow, idxCol]);
                        xyr.SetBandValues(idxRow, idxCol, pt.ToFloatArray());
                    }
                }
            }

            return xyr;
        }

        private Matrix ObservationToRoot(Observation obs, FrameCache frameCache)
        {
            Frame obsFrame = frameCache.GetFrame(obs.FrameName);
            Frame sitedriveFrame = frameCache.GetFrame(obsFrame.ParentName);

            UncertainRigidTransform obsToSiteDrive = FrameTransform.Find(pipeline, obsFrame).Transform;
            UncertainRigidTransform siteDriveToRoot = FrameTransform.Find(pipeline, sitedriveFrame).Transform;
     
            UncertainRigidTransform transform = obsToSiteDrive * siteDriveToRoot;
            return transform.Mean;
        }

        /// <summary>
        /// creates a point cloud mesh from a set of pointcloud input textures
        /// normals are scaled by confidence as the poisson reconstruction tool 
        /// uses the magnitude of the normal to indicate confidence
        /// </summary>
        /// <returns>a point cloud mesh (position and normals) in the root frame of the alignment</returns>
        private Mesh BuildPointCloudMesh(PointCloudInput pcInput, FrameCache frameCache, ObservationCache obsCache)
        {
            Mesh ptsRoverFrame = new Mesh(hasNormals: true);
            int imgWidth = pcInput.Points.PDSImage.Metadata.Width;
            int imgHeight = pcInput.Points.PDSImage.Metadata.Height;
            for (int idxRow = 0; idxRow < imgHeight; idxRow++)
            {
                for (int idxCol = 0; idxCol < imgWidth; idxCol++)
                {
                    if (pcInput.Points.GeneratedImage.IsInvalid(idxRow, idxCol) ||
                        pcInput.Normals.GeneratedImage.IsInvalid(idxRow, idxCol) ||
                        pcInput.RoverMask.GeneratedImage.IsInvalid(idxRow, idxCol) ||
                        pcInput.Confidence.GeneratedImage.IsInvalid(idxRow, idxCol))
                    {
                        continue;
                    }

                    float confidence = pcInput.Confidence.GeneratedImage[0, idxRow, idxCol];
                    ptsRoverFrame.Vertices.Add(new Vertex(new Vector3(pcInput.Points.GeneratedImage[0, idxRow, idxCol],
                                                                       pcInput.Points.GeneratedImage[1, idxRow, idxCol],
                                                                       pcInput.Points.GeneratedImage[2, idxRow, idxCol]),
                                                          new Vector3(pcInput.Normals.GeneratedImage[0, idxRow, idxCol] * confidence,
                                                                       pcInput.Normals.GeneratedImage[1, idxRow, idxCol] * confidence,
                                                                       pcInput.Normals.GeneratedImage[2, idxRow, idxCol] * confidence)));
                }
            }

            if (ptsRoverFrame.Vertices.Count == 0)
            {
                LogWarn("point cloud contributed no data " + pcInput.Points.Obs.FrameName);
                return null;
            }

            Matrix observationToRoot = ObservationToRoot(pcInput.Points.Obs, frameCache);
            return Mesh.Transformed(ptsRoverFrame, observationToRoot);
        }

        /// <summary>
        /// filters all the cached rover observations for ones that are valid for reconstruction
        ///     and groups them into a struct by frame
        /// </summary>
        /// <returns></returns>
        private static List<PointCloudObservations> CollectPointCloudInputs(ObservationCache obsCache,
                                                                            FrameCache frameCache)
        {
            // collect data to build point clouds
            List<PointCloudObservations> pointCloudInputs = new List<PointCloudObservations>();
            string obsTypePoints = ObservationType.Points.ToString();
            string obsTypeNormals = ObservationType.Normals.ToString();
            string obsTypeRoverMask = ObservationType.RoverMask.ToString();
            foreach (string frameName in obsCache.GetAllFramesWithObservations())
            {
                List<RoverObservation> obsForFrame =
                    obsCache.GetAllObservationsForFrame(frameCache.GetFrame(frameName))
                    .Cast<RoverObservation>()
                    .ToList();
                obsForFrame.Sort(MSLProject.RoverObservationComparison);

                PointCloudObservations pcInput;
                pcInput.PointsObs = obsForFrame.Find(x => x.ObservationType == obsTypePoints);
                if (pcInput.PointsObs == null)
                    continue;

                // temporarily suppress mastcam point cloud data until validated (Issue #261)
                if (pcInput.PointsObs.Sensor == RoverProductCamera.MastcamLeft.ToString() || pcInput.PointsObs.Sensor == RoverProductCamera.MastcamRight.ToString())
                    continue;

                pcInput.NormalObs = obsForFrame.Find(x => x.ObservationType == obsTypeNormals);
                if (pcInput.NormalObs == null)
                    continue;

                pointCloudInputs.Add(pcInput);
            }

            return pointCloudInputs;
        }
    }
}
