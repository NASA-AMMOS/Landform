using System;
using System.Collections.Generic;
using System.Linq;
using log4net;
using OPS.Pipeline.TileServer;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Plumbing;
using OPS.Util;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline.MeshWorker
{
    /// <summary>
    /// message sent to create a large mesh from input data 
    /// and upload it as the tiling input
    /// </summary>
    public class BuildTilingInputMessage : TilingQueueMessage
    {
        public BuildTilingInputMessage() { }

        public BuildTilingInputMessage(string projectName) : base(projectName)
        {
        }
    }

    /// <summary>
    /// create a large mesh from input data and uploads it as the tiling input
    /// </summary>
    public class BuildTilingInput
    {
        static ILog logger = LogManager.GetLogger(typeof(BuildTilingInput));

        StartWorker pipeline;
        BuildTilingInputMessage message;
        Options options;

        struct Options
        {
            public string AlignmentProjectName; //the project name that contains the alignment data to be used with this tiling project (code sets to tiling project name, hardcode locally to use previous alignment project)
            public int EstimatedItemSizeBytes;  //dynamo throttling: estimated item size (table size/ num items)
            public int TableReadCapacity;       //dynamo throttling: provisioned read capacity
        }

        public BuildTilingInput(BuildTilingInputMessage message, StartWorker pipeline)
        {
            this.pipeline = pipeline;
            this.message = message;

            this.options.AlignmentProjectName = message.ProjectName;

            //Issue #268: query/build these values from AWS apis
            this.options.EstimatedItemSizeBytes = 820;
            this.options.TableReadCapacity = 50;
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
            logger.Info("Building tiling input...");

            //cache data needed to build pointcloud
            FrameCache frameCache = new FrameCache(pipeline.DynamoContext, options.AlignmentProjectName);
            RoverObservationCache obsCache = new RoverObservationCache(pipeline.DynamoContext, options.AlignmentProjectName, options.EstimatedItemSizeBytes, options.TableReadCapacity);
            obsCache.FillCache(onlyReconstructionObs: true);

            //find the best observations to use for each point cloud
            List<PointCloudObservations> pointCloudObservations = CollectPointCloudInputs(obsCache);
            if (pointCloudObservations.Count == 0)
            {
                logger.Error("no observations were found to build a point cloud");
                return 1;
            }
            
            //accumulate the large point cloud
            Mesh aggregatePointCloud = new Mesh(hasNormals: true);
            for (int idx = 0; idx < pointCloudObservations.Count; idx++)
            {
                logger.InfoFormat("Building point cloud {0}/{1} ({2})%): {3}", idx+1, pointCloudObservations.Count, (int)(100 * idx / (float)pointCloudObservations.Count), pointCloudObservations[idx].PointsObs.FrameName);

                PointCloudInput pcImgs = GetPointCloudInput(pointCloudObservations[idx]);
                Mesh pointCloud = BuildPointCloudMesh(pcImgs, frameCache, obsCache);
                if (pointCloud != null)
                {
                    aggregatePointCloud.MergeWith(new Mesh[] { pointCloud }, false);
                }
            }
            
            // build the large mesh from the aggregate point cloud using poisson reconstruction
            if (aggregatePointCloud.Vertices.Count == 0)
            {
                logger.Error("Aggregate point cloud contains no points");
                return 1;
            }
          
            logger.Info("Reconstructing point cloud: " + aggregatePointCloud.Vertices.Count() + " vertices");
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
                logger.Error("Point cloud failed to reconstruct");
                return 1;
            }

            //upload mesh
            string meshName = "FullMesh";
            string s3MeshOutputUrl = TileServerConfig.Instance.InputUrl(message.ProjectName, meshName + ".ply");
            TemporaryFile.GetAndDelete(".ply", tempFile =>
            {
                logger.Info("Uploading mesh: " + s3MeshOutputUrl);
                surfacedMesh.Save(tempFile);
                this.pipeline.Storage(s3MeshOutputUrl).UploadFile(tempFile, s3MeshOutputUrl);
            });

            //create a tiling input
            TilingProject tilingProject = TilingProject.Find(this.pipeline.DynamoContext, message.ProjectName);
            TilingInput.Create(this.pipeline.DynamoContext, meshName, tilingProject, s3MeshOutputUrl, null, null);
            
            //indicate successs to the tiling server master
            pipeline.CompletionQueue.Enqueue(new BuildTilingInputMessage(this.message.ProjectName));
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
        private PointCloudInput GetPointCloudInput(PointCloudObservations pointCloudObservations)
        {
            PointCloudInput pct = new PointCloudInput();

            // build the point cloud input, generated result is an XYR (3d points in rover frame) with a mask marking invalid pixels
            pct.Points.Obs = pointCloudObservations.PointsObs;
            pct.Points.PDSImage = GetObservationImage(pointCloudObservations.PointsObs, RoverProductType.Range);
            pct.Points.GeneratedImage = ConvertRNGToXYR(pct.Points.PDSImage);

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
                throw new ArgumentException("mismatched resolutions across points and normals");
            }

            return pct;
        }

        /// <summary>
        /// pulls down the image for an observation from S3 and does basic validation
        /// </summary>
        private Image GetObservationImage(RoverObservation obs, params RoverProductType[] expectedProductTypes)
        {
            S3ImageRef s3ref = new S3ImageRef(obs.Url);
            Image img = this.pipeline.Load(s3ref, false, ImageConverters.PassThrough);
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
        /// until mission products giving useful error estimates are available in s3
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
        static public Image ConvertRNGToXYR(Image img)
        {
            //validate assumptions about input data for rng images
            PDSParser rangePDR = new PDSParser((PDSMetadata)img.Metadata);
            if (rangePDR.DerivedImageRefFrame != PDSParser.ReferenceCoordinateFrame.RoverNav)
            {
                if (rangePDR.CameraModelRefFrame != PDSParser.ReferenceCoordinateFrame.RoverNav)
                    throw new NotImplementedException("non-rover frame camera model not supported yet");
            
                CAHV cahv = img.CameraModel as CAHV;
                if (cahv == null)
                    throw new NotImplementedException("only cahv, cahvor, cahvore camera models handled currently");

                // find the range data's origin in rover frame
                Vector3 cameraPosRover = Vector3.Transform(rangePDR.RangeOrigin, RoverCoordinateSystem.SiteToRover(rangePDR.RoverOriginRotation, rangePDR.OriginOffset));

                //verify we can use the camera model's position as the origin for the range data
                if (!Vector3.AlmostEqual(cameraPosRover, cahv.C, 0.0005))
                    throw new NotImplementedException("only expecting range maps from the camera's location");
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

        /// <summary>
        /// creates a point cloud mesh from a set of pointcloud input textures
        /// normals are scaled by confidence as the poisson reconstruction tool 
        /// uses the magnitude of the normal to indicate confidence
        /// </summary>
        /// <returns>a point cloud mesh (position and normals) in the root frame of the alignment</returns>
        private Mesh BuildPointCloudMesh(PointCloudInput pcInput, FrameCache frameCache, RoverObservationCache obsCache)
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
                logger.Warn("point cloud contributed no data " + pcInput.Points.Obs.FrameName);
                return null;
            }

            Matrix observationToRoot = BuildFromAlignment.ObservationToRoot(this.pipeline.DynamoContext, pcInput.Points.Obs, frameCache).Mean;
            return Mesh.Transformed(ptsRoverFrame, observationToRoot);
        }

        /// <summary>
        /// filters all the cached rover observations for ones that are valid for reconstruction
        ///     and groups them into a struct by frame
        /// </summary>
        /// <returns></returns>
        private static List<PointCloudObservations> CollectPointCloudInputs(RoverObservationCache obsCache)
        {
            // collect data to build point clouds
            List<PointCloudObservations> pointCloudInputs = new List<PointCloudObservations>();
            string obsTypePoints = ObservationType.Points.ToString();
            string obsTypeNormals = ObservationType.Normals.ToString();
            string obsTypeRoverMask = ObservationType.RoverMask.ToString();
            foreach (string frameName in obsCache.GetFrameNamesWithObservations())
            {
                List<RoverObservation> obsForFrame = obsCache.GetObsByFrame(frameName);
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
