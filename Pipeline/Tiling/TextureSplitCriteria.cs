using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;
using OPS.RayTrace;

namespace OPS.Pipeline
{
    public class CameraInstance
    {
        public Matrix cameraToMesh;
        public Matrix meshToCamera;
        public CameraModel cameraModel;
        public ConvexHull hullInMesh;
        public int widthPixels;
        public int heightPixels;
    };

    public class SplitByTextureOpts
    {
        public double pctPixelsToTest;              // how densely should the uv atlas pixels be tested be sampled (1.0: test all atlas pixels, 0.5: test half atlas pixels, etc)
        public double pctSampledPixelsSatisfied;    // of all the pixels sampled for a given source texture, what percentage of them need to be above the split criteria (1.0 any pixel that needs a split is enough, 0.5 at least half the pixels need a split, etc)
        public double splitPixelTexelRatio;         // valid values > 1.0. a value of 2.0 would mean if 2 source textures are being squeezed into a single output texture that incur a split

        public int tileResolution;
        public CameraInstance[] cameraInstances;
        public SceneCaster scInMesh;
    }

    abstract public class TextureSplitCriteria : ITileSplitCriteria
    {
        protected SplitByTextureOpts options;

        public TextureSplitCriteria(SplitByTextureOpts opts)
        {
            options = opts;

            if (opts.pctPixelsToTest <= 0 || opts.pctPixelsToTest > 1)
                throw new Exception("invalid pctPixelsToTest option");

            if (opts.pctSampledPixelsSatisfied <= 0 || opts.pctSampledPixelsSatisfied > 1)
                throw new Exception("invalid pctSampledPixelsServiced option");

            if (opts.splitPixelTexelRatio <= 1.0)
                throw new Exception("invalid subsamplingTriggeringSplit option");
        }

        public bool ShouldSplit(MeshOperator meshOperator, BoundingBox areaOfInterest)
        {
            // coarse frustum test against the bounding box
            List<CameraInstance> intersectingCameras = options.cameraInstances.Where(ci => ci.hullInMesh.Intersects(areaOfInterest)).ToList();

            //no textures would be used on this mesh, no need to split
            if (intersectingCameras.Count == 0)
                return false;

            // may have too few faces to ever service texture resolution (output atlas too low res)
            Mesh clippedMesh = meshOperator.Clip(areaOfInterest);
            if (clippedMesh.Faces.Count == 2)
                return false;

            // finer frustum test: get all observations that intersect mesh hull
            ConvexHull clippedHull = new ConvexHull(clippedMesh);
            intersectingCameras = intersectingCameras.Where(ci => clippedHull.Intersects(ci.hullInMesh)).ToList();

            //no textures would be used on this mesh, no need to split
            if (intersectingCameras.Count == 0)
                return false;

            if (!GetCandidateDestTexelArea(clippedMesh, out double dstPixelsArea))
                return false;

            if (!GetCandidateSourcePixelArea(clippedMesh, clippedHull, intersectingCameras, out double srcPixelsArea))
                return false;

            double ratioOfSrcToDest = srcPixelsArea / dstPixelsArea;
            return ratioOfSrcToDest >= options.splitPixelTexelRatio;
        }


        //single pixel api, for a representative pixel what is the ratio of source to dest pixel areas
        protected abstract bool GetCandidateSourcePixelArea(Mesh clippedMesh, ConvexHull clippedHull, List<CameraInstance> intersectingCameras, out double srcPixelArea);
        protected abstract bool GetCandidateDestTexelArea(Mesh clippedMesh, out double dstPixelArea);
    }

    public class TextureSplitCriteriaBackproject : TextureSplitCriteria
    {
        public TextureSplitCriteriaBackproject(SplitByTextureOpts opts) : base(opts)
        { }

        protected override bool GetCandidateDestTexelArea(Mesh clippedMesh, out double dstPixelArea)
        {
            //current approach is based on maximum of all the images, of the user specified percentage of tested pixels
            // sourcepixels per single output pixel. this function returns a single pixel area to match the single source pixel area
            dstPixelArea = 1;
            return true;
        }

        protected override bool GetCandidateSourcePixelArea(Mesh clippedMesh, ConvexHull clippedHull, List<CameraInstance> intersectingCameras, out double srcPixelArea)
        {
            srcPixelArea = 0;

            //generate an atlas for the mesh if needed
            if (!clippedMesh.HasUVs)
            {
                try
                {
                    clippedMesh = UVAtlas.Atlas(clippedMesh, options.tileResolution, options.tileResolution);
                    if (clippedMesh == null)
                        return false;
                }
                catch
                {
                    //TODO: not being able to atlas can be caused by mesh complexity, which might be helped by a split 
                    // https://github.jpl.nasa.gov/OnSight/Landform/issues/826
                    //returning false in case there's a mesh that wont atlas (degenerate triangles?)
                    //this would recurse down to single triangle tiles
                    return false;
                }
            }

            //choose a sub-set of points (for perf) from the output atlas texture to test
            MeshOperator clippedOp =
                new MeshOperator(clippedMesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);
            List<PixelPoint> ptsToTest =
                clippedOp.SubsampleUVSpace(options.pctPixelsToTest, options.tileResolution, options.tileResolution);

            //record the pixel area of the image that would be used to texture the mesh for each output atlas pixel
            Dictionary<CameraInstance, List<double>> srcAreaByCamera = new Dictionary<CameraInstance, List<double>>();
            foreach (var destPixelPt in ptsToTest)
            {
                //if the points are spilling onto other tiles, they aren't great candidates for testing.
                // In addition to handling cases where you are peeking through a valley or keyhole in the terrain
                // and all points are landing on other mesh tiles, this is a performance optimization.
                if (!clippedHull.Contains(destPixelPt.Point)) 
                {
                    continue;
                }

                //find the camera that provides the best pixel density for this sample
                //(would be the texture we would use at this location)
                if (!GetBestCameraByPixelDensity(intersectingCameras, clippedHull, clippedOp.Bounds, destPixelPt,
                                                 out CameraInstance bestCamera))
                {
                    continue;
                }

                // calculate src pixels area contributing to the pixel  
                Vector2[] pixelCorners = Image.GetPixelCorners(destPixelPt.Pixel);
                var uvsCorners =
                    pixelCorners.Select(c => Image.PixelToUV(c, options.tileResolution, options.tileResolution));
                var destPixelMeshPositions =
                    uvsCorners
                    .Select(uv => clippedOp.UVToBarycentric(uv))
                    .Where(bary => bary != null)
                    .Select(bary => bary.Position);
                var srcPixels =
                    destPixelMeshPositions
                    .Select(meshPos => ProjectedPixelDistances.GetCameraPixelForMeshPosition(options.scInMesh, bestCamera.cameraModel,
                                                                     bestCamera.cameraToMesh, bestCamera.meshToCamera,
                                                                     bestCamera.hullInMesh, meshPos,
                                                                     bestCamera.widthPixels, bestCamera.heightPixels));

                // if all 4 pixels landed in the source image, find their area in pixels
                if (4 == srcPixels.Where(x => x.HasValue).Count())
                {
                    double srcPixelAreaForDestPixel = Image.CalculateQuadPixelArea(srcPixels.Select(x => x.Value).ToArray());
                    if (!srcAreaByCamera.ContainsKey(bestCamera))
                    {
                        srcAreaByCamera.Add(bestCamera, new List<double>() { srcPixelAreaForDestPixel });
                    }
                    else
                    {
                        srcAreaByCamera[bestCamera].Add(srcPixelAreaForDestPixel);
                    }
                }
            }

            //these area values represent the number of pixels in the src textures
            //being squished or streched to fill the destination texture pixels
            //ideally we would like that number to be 1, but we are at the mercy of the uvatlas
            //which can choose to compress an areas texture sampling based solely on geometry.
            //if the area is greater than 1 at the percentage of pixels requested we should subdivide
            //and try again with the new leaf tile
            double maxPixelsTested = double.MinValue;
            foreach (var key in srcAreaByCamera.Keys)
            {
                var pixelsTested = srcAreaByCamera[key];
                if (pixelsTested == null)
                {
                    continue;
                }

                // is current atlas fine for texture resolution
                if (!pixelsTested.Any(x => x > 1.0))
                {
                    continue;
                }

                //the option specifies the percentage of pixels that need to be satisfied to avoid a split           
                pixelsTested.Sort();
                int idxToTest = (int)((pixelsTested.Count - 1) * options.pctSampledPixelsSatisfied);

                if (pixelsTested[idxToTest] > maxPixelsTested)
                {
                    maxPixelsTested = pixelsTested[idxToTest];
                }
            }

            srcPixelArea = maxPixelsTested;
            return maxPixelsTested != double.MinValue;
        }

        private bool GetBestCameraByPixelDensity(List<CameraInstance> candidateCameras, ConvexHull meshHull,
                                                    BoundingBox meshBounds, PixelPoint pxlPt, out CameraInstance bestCamera)
        {
            bestCamera = null;

            double minSpread = double.MaxValue;
            bestCamera = new CameraInstance();
            foreach (var camInst in candidateCameras)
            {
                var srcPixel = ProjectedPixelDistances.GetCameraPixelForMeshPosition(options.scInMesh, camInst.cameraModel, camInst.cameraToMesh,
                                                             camInst.meshToCamera, camInst.hullInMesh,
                                                             pxlPt.Point, camInst.widthPixels, camInst.heightPixels);

                if (!srcPixel.HasValue)
                    continue;

                //Issue #523: want median or average in case glancing angle?
                //want a term that looks for consistancy in spacing? implies dead on?
                double curSpread = ProjectedPixelDistances.GetMinPixelSpreadInMeters(options.scInMesh, camInst.cameraModel,
                                                             camInst.cameraToMesh,
                                                             srcPixel.Value, pxlPt.Point, meshBounds,
                                                             camInst.widthPixels, camInst.heightPixels);
                if (curSpread < minSpread)
                {
                    minSpread = curSpread;
                    bestCamera = camInst;
                }
            }

            return minSpread != double.MaxValue;
        }
    }

    public class TextureSplitCriteriaApproximate : TextureSplitCriteria
    {
        public const double APPROX_TEXTURE_UTILIZATION = 0.5;

        public TextureSplitCriteriaApproximate(SplitByTextureOpts opts) : base(opts)
        {
        }

        protected override bool GetCandidateDestTexelArea(Mesh clippedMesh, out double dstPixelArea)
        {
            dstPixelArea = 0;
            double clippedArea = clippedMesh.SurfaceArea();
            if (clippedArea <= 0)
            {
                return false;
            }

            double numTexels = options.tileResolution * options.tileResolution;

            //the uv atlas wastes some amount of pixels on gutter, accounted for here by APPROX_TEXTURE_UTILIZATION
            // the uv atlas also allocates area unequally in the atlas, could spend 80% of the pixels
            // on 20% of the area (not accounted for here)
            dstPixelArea = APPROX_TEXTURE_UTILIZATION * numTexels / clippedArea;
            return true;
        }
        protected override bool GetCandidateSourcePixelArea(Mesh clippedMesh, ConvexHull clippedHull, List<CameraInstance> intersectingCameras, out double srcPixelArea)
        {
            srcPixelArea = 0;

            double minAreaPerPixelInMeters = double.MaxValue;
            foreach (var camInst in intersectingCameras)
            {
                //find the closest point on the mesh to the camera
                // this will be used for the estimate of the pixel density
                // in an attempt to be conservative
                Vector3 camInMesh = Vector3.Transform(((CAHV)camInst.cameraModel).C, camInst.cameraToMesh);
                double minDistSq = double.MaxValue;
                foreach (var vert in clippedMesh.Vertices)
                {
                    double curDistSq = Vector3.DistanceSquared(camInMesh, vert.Position);
                    if (curDistSq < minDistSq)
                    {
                        minDistSq = curDistSq;
                    }
                }

                if (minDistSq == double.MaxValue)
                {
                    continue;
                }
                double minDist = Math.Sqrt(minDistSq);

                //calculate the distance between pixel corners and use the diagonal to approximate the area 
                // (uses a square pixel approximation) 
                var corners = Image.GetPixelCorners(new Vector2(camInst.widthPixels / 2.0, camInst.heightPixels / 2.0));
                Vector3 ptUpperLeftCorner = camInst.cameraModel.Unproject(corners.ElementAt(0), minDist);
                Vector3 ptUpperRightCorner = camInst.cameraModel.Unproject(corners.ElementAt(1), minDist);
                Vector3 ptLowerRightCorner = camInst.cameraModel.Unproject(corners.ElementAt(2), minDist);
                Vector3 ptLowerLeftCorner = camInst.cameraModel.Unproject(corners.ElementAt(3), minDist);
                double curAreaPerPixelInMeters = Vector3.Distance(ptUpperLeftCorner, ptUpperRightCorner) * Vector3.Distance(ptUpperLeftCorner, ptLowerLeftCorner);
                if (curAreaPerPixelInMeters < minAreaPerPixelInMeters)
                {
                    minAreaPerPixelInMeters = curAreaPerPixelInMeters;
                }
            }

            //convert area in m^2 of 1 pixel to number of pixels in 1 m^2
            srcPixelArea = 1 / minAreaPerPixelInMeters;
            return minAreaPerPixelInMeters != double.MaxValue;
        }
    }
}