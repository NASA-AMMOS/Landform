using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Util;
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
        // how densely should the destination texels be sampled
        public double pctPixelsToTest = TilingDefaults.TEX_SPLIT_PERCENT_TO_TEST;

        // of all the pixels sampled for a given source texture, what percentage of them need to suggest a split
        public double pctPixelsSatisfied = TilingDefaults.TEX_SPLIT_PERCENT_SATISFIED;

        // valid values >= 1; 2 would mean if > 2 source pixels are squeezed into a single output texel then split
        public double maxPixelsPerTexel = TilingDefaults.TEX_SPLIT_MAX_PIXELS_PER_TEXEL;

        public int maxTileResolution = TilingDefaults.MAX_TILE_RESOLUTION;
        public double maxTexelsPerMeter = TilingDefaults.MAX_TEXELS_PER_METER;
        public double maxTextureStretch = TilingDefaults.MAX_TEXTURE_STRETCH;
        public bool powerOfTwoTextures = TilingDefaults.POWER_OF_TWO_TEXTURES;

        public CameraInstance[] cameraInstances;

        public SceneCaster scInMesh;
        public double raycastTolerance;

        public bool redoUVs;

        public Action<string> warn;
    }

    abstract public class TextureSplitCriteria : ITileSplitCriteria
    {
        protected SplitByTextureOpts options;
        protected bool spewProgress;

        public TextureSplitCriteria(SplitByTextureOpts opts)
        {
            options = opts;

            if (opts.pctPixelsToTest <= 0 || opts.pctPixelsToTest > 1)
            {
                throw new Exception("invalid pctPixelsToTest option");
            }

            if (opts.pctPixelsSatisfied <= 0 || opts.pctPixelsSatisfied > 1)
            {
                throw new Exception("invalid pctPixelsSatisfied option");
            }

            if (opts.maxPixelsPerTexel < 1)
            {
                throw new Exception("invalid maxPixelsPerTexel option");
            }
        }

        public bool ShouldSplit(BoundingBox bounds, params MeshOperator[] meshOps)
        {
            //TODO ISSUE 1038:  add the resolution of orbital to this decision

            if (meshOps.Length != 1)
            {
                throw new ArgumentException("TextureSplitCriteria can only operate on a single mesh");
            }
            var meshOperator = meshOps[0];

            // coarse frustum test against the bounding box
            var intersectingCameras = options.cameraInstances
                .Where(ci => ci.hullInMesh != null && ci.hullInMesh.Intersects(bounds))
                .ToList();
            if (intersectingCameras.Count == 0)
            {
                return false;
            }

            Mesh clippedMesh = meshOperator.Clipped(bounds);
            if (!clippedMesh.HasFaces)
            {
                return false;
            }

            // finer frustum test: get all observations that intersect mesh hull
            ConvexHull clippedHull = ConvexHull.CreateWithFallback(clippedMesh);
            intersectingCameras = intersectingCameras.Where(ci => clippedHull.Intersects(ci.hullInMesh)).ToList();

            //no textures would be used on this mesh, no need to split
            if (intersectingCameras.Count == 0)
            {
                return false;
            }

            double meshArea = clippedMesh.SurfaceArea();
            int texRes = SceneNodeTilingExtensions.
                GetTileResolution(meshArea, options.maxTileResolution, options.maxTexelsPerMeter,
                                  options.powerOfTwoTextures);

            if (texRes < options.maxTileResolution)
            {
                return false;
            }

            //for a representative texel estimate the ratio of observation pixel area to tile texel area
            //different implementations may have different definitions of "area"
            //but it doesn't matter because we're just going to take a ratio, so that will divide out
            if (!GetTileTexelsPerArea(clippedMesh, texRes, meshArea, out double texels))
            {
                return false;
            }

            if (!GetObservationPixelsPerArea(clippedMesh, texRes, clippedHull, intersectingCameras, out double pixels))
            {
                return false;
            }

            return (pixels / texels) > options.maxPixelsPerTexel;
        }

        protected abstract bool GetObservationPixelsPerArea(Mesh clippedMesh, int texRes, ConvexHull clippedHull,
                                                            List<CameraInstance> intersectingCameras,
                                                            out double pixels);

        protected abstract bool GetTileTexelsPerArea(Mesh clippedMesh, int texRes, double meshArea, out double texels);
    }

    public class TextureSplitCriteriaBackproject : TextureSplitCriteria
    {
        const double MESH_HULL_TEST_EPSILON = 0.00001;

        public TextureSplitCriteriaBackproject(SplitByTextureOpts opts) : base(opts)
        {
            spewProgress = true;
        }

        protected override bool GetTileTexelsPerArea(Mesh clippedMesh, int texRes, double meshArea, out double texels)
        {
            texels = 1; //this implementation always computes observation pixel area for one tile texel
            return true;
        }

        protected override bool GetObservationPixelsPerArea(Mesh clippedMesh, int texRes, ConvexHull clippedHull,
                                                            List<CameraInstance> intersectingCameras,
                                                            out double pixels)
        {
            pixels = 0;

            //generate an atlas for the mesh if needed
            if (!clippedMesh.HasUVs || options.redoUVs)
            {
                try
                {
                    if (!UVAtlas.Atlas(clippedMesh, texRes, texRes, maxStretch: options.maxTextureStretch,
                                       logger: new ThunkLogger() { Warn = options.warn }))
                    {
                        return false;
                    }
                }
                catch {}
                if (clippedMesh == null)
                {
                    //TODO: not being able to atlas can be caused by mesh complexity, which might be helped by a split 
                    // https://github.jpl.nasa.gov/OnSight/Landform/issues/826
                    //returning false in case there's a mesh that wont atlas (degenerate triangles?)
                    //this would recurse down to single triangle tiles
                    return false;
                }
            }
            else
            {
                clippedMesh.RescaleUVsForTexture(texRes, texRes, options.maxTextureStretch);
            }

            //choose a sub-set of points (for perf) from the output atlas texture to test
            MeshOperator clippedOp =
                new MeshOperator(clippedMesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);
            List<PixelPoint> ptsToTest = clippedOp.SubsampleUVSpace(options.pctPixelsToTest, texRes, texRes);

            var clippedCaster = new SceneCaster();
            clippedCaster.AddMesh(clippedMesh, null, Matrix.Identity);
            clippedCaster.Build();

            //record the pixel area of the image that would be used to texture the mesh for each output atlas pixel
            var srcAreaByCamera = new Dictionary<CameraInstance, List<double>>();
            foreach (var destPixelPt in ptsToTest)
            { 
                //if the points are spilling onto other tiles, they aren't great candidates for testing.
                // In addition to handling cases where you are peeking through a valley or keyhole in the terrain
                // and all points are landing on other mesh tiles, this is a performance optimization.
                if (!clippedHull.Contains(destPixelPt.Point, MESH_HULL_TEST_EPSILON))
                {
                    continue;
                }

                //find the camera that provides the best pixel density for this sample
                //(would be the texture we would use at this location)
                if (!GetBestCameraByPixelDensity(intersectingCameras, clippedCaster, clippedHull, clippedOp.Bounds,
                                                 destPixelPt, out CameraInstance bestCamera))
                {
                    continue;
                }

                // calculate src pixels area contributing to the pixel  
                Vector2[] dstPixelCorners = Image.GetPixelCorners(destPixelPt.Pixel);
                var dstUVCorners = dstPixelCorners.Select(c => Image.PixelToUV(c, texRes, texRes));
                var destPixelMeshPositions =
                    dstUVCorners
                    .Select(uv => clippedOp.UVToBarycentric(uv))
                    .Where(bary => bary != null)
                    .Select(bary => bary.Position);
                var srcPixels =
                    destPixelMeshPositions
                    .Select(meshPos => ProjectedPixelDistances.
                            GetCameraPixelForMeshPosition(options.scInMesh, bestCamera.cameraModel,
                                                          bestCamera.cameraToMesh, bestCamera.meshToCamera,
                                                          bestCamera.hullInMesh, meshPos,
                                                          bestCamera.widthPixels, bestCamera.heightPixels))
                    .Where(x => x.HasValue);

                // if enough pixels landed in the source image, find their area in pixels
                int countValidPixels = srcPixels.Count();
                double srcPixelArea = 0;
                if (4 == countValidPixels)
                {
                    srcPixelArea = Image.CalculateQuadPixelArea(srcPixels.Select(x => x.Value).ToArray());
                }
                else if (3 == countValidPixels)
                {
                    srcPixelArea = Image.CalculateTriPixelArea(srcPixels.Select(x => x.Value).ToArray());
                }

                if (srcPixelArea <= 0)
                {
                    continue;
                }

                if (!srcAreaByCamera.ContainsKey(bestCamera))
                {
                    srcAreaByCamera.Add(bestCamera, new List<double>() { srcPixelArea });
                }
                else
                {
                    srcAreaByCamera[bestCamera].Add(srcPixelArea);
                }
            }

            if (srcAreaByCamera.Count == 0)
            {
                return false;
            }

            //these area values represent the number of pixels in the src textures
            //being squished or streched to fill the destination texture pixels
            //ideally we would like that number to be 1, but we are at the mercy of the uvatlas
            //which can choose to compress an areas texture sampling based solely on geometry.
            //if the area is greater than 1 at the percentage of pixels requested we should subdivide
            //and try again with the new leaf tile
            double maxSrcPixelArea = double.MinValue;
            foreach (var key in srcAreaByCamera.Keys)
            {
                var srcPixelAreas = srcAreaByCamera[key];

                //the option specifies the percentage of pixels that need to be satisfied to avoid a split           
                srcPixelAreas.Sort();
                int idxToTest = (int)((srcPixelAreas.Count - 1) * options.pctPixelsSatisfied);

                if (srcPixelAreas[idxToTest] > maxSrcPixelArea)
                {
                    maxSrcPixelArea = srcPixelAreas[idxToTest];
                }
            }

            pixels = maxSrcPixelArea;
            return true;
        }

        private bool GetBestCameraByPixelDensity(List<CameraInstance> candidateCameras, SceneCaster meshCaster,
                                                 ConvexHull meshHull, BoundingBox meshBounds, PixelPoint pxlPt,
                                                 out CameraInstance bestCamera)
        {
            bestCamera = null;

            double minSpread = double.MaxValue;
            bestCamera = new CameraInstance();
            foreach (var camInst in candidateCameras)
            {
                var srcPixel = ProjectedPixelDistances.
                    GetCameraPixelForMeshPosition(options.scInMesh, camInst.cameraModel, camInst.cameraToMesh,
                                                  camInst.meshToCamera, camInst.hullInMesh,
                                                  pxlPt.Point, camInst.widthPixels, camInst.heightPixels);

                if (!srcPixel.HasValue)
                {
                    continue;
                }

                //Issue #523: want median or average in case glancing angle?
                //want a term that looks for consistancy in spacing? implies dead on?
                double curSpread = ProjectedPixelDistances.
                    GetMinPixelSpreadInMeters(meshBounds, meshCaster, options.scInMesh, camInst.cameraModel,
                                              camInst.cameraToMesh, srcPixel.Value, pxlPt.Point,
                                              camInst.widthPixels, camInst.heightPixels, options.raycastTolerance);
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

        public TextureSplitCriteriaApproximate(SplitByTextureOpts opts) : base(opts) {}

        protected override bool GetTileTexelsPerArea(Mesh clippedMesh, int texRes, double meshArea, out double texels)
        {
            texels = 0;
            if (meshArea <= 0)
            {
                return false;
            }

            double numTexels = texRes * texRes;

            //the uv atlas wastes some amount of pixels on gutter, accounted for here by APPROX_TEXTURE_UTILIZATION
            // the uv atlas also allocates area unequally in the atlas, could spend 80% of the pixels
            // on 20% of the area (not accounted for here)
            texels = APPROX_TEXTURE_UTILIZATION * numTexels / meshArea; //return texels per square meter

            return true;
        }

        protected override bool GetObservationPixelsPerArea(Mesh clippedMesh, int texRes, ConvexHull clippedHull,
                                                            List<CameraInstance> intersectingCameras,
                                                            out double pixels)
        {
            pixels = 0;

            double minMetersPerPixel = double.MaxValue;
            foreach (var camInst in intersectingCameras)
            {
                // project grid of 25 central rays from camera to estimate closest distance to mesh
                var samples = new List<Vector2>(25);
                int rowSkip = camInst.heightPixels / 6;
                int colSkip = camInst.widthPixels / 6;
                for (int r = rowSkip; r < camInst.heightPixels; r += rowSkip)
                {
                    for (int c = colSkip; c < camInst.heightPixels; c += colSkip)
                    {
                        samples.Add(new Vector2(c, r));
                    }
                }

                var pts = ProjectedPixelDistances.GetMeshPositionsForCameraPixels(clippedMesh.Bounds(),
                                                                                  options.scInMesh, options.scInMesh,
                                                                                  camInst.cameraModel,
                                                                                  camInst.cameraToMesh, samples,
                                                                                  options.raycastTolerance);
                if (pts.Count < 1)
                {
                    continue;
                }

                Vector3 camInMesh = Vector3.Transform(((CAHV)camInst.cameraModel).C, camInst.cameraToMesh);
                double minDist = pts.Min(pt => Vector3.Distance(camInMesh, pt));

                // use a square pixel approximation
                Vector2 []corners = Image.GetPixelCorners(new Vector2(camInst.widthPixels / 2.0,
                                                                      camInst.heightPixels / 2.0));
                Vector3 ptUpperLeftCorner = camInst.cameraModel.Unproject(corners[0], minDist);
                Vector3 ptUpperRightCorner = camInst.cameraModel.Unproject(corners[1], minDist);
                Vector3 ptLowerRightCorner = camInst.cameraModel.Unproject(corners[2], minDist);
                Vector3 ptLowerLeftCorner = camInst.cameraModel.Unproject(corners[3], minDist);
                double projectedWidth = Vector3.Distance(ptUpperLeftCorner, ptUpperRightCorner);
                double projectedHeight = Vector3.Distance(ptUpperLeftCorner, ptLowerLeftCorner);
                double curAreaPerPixelInMeters = projectedWidth * projectedHeight;
                if (curAreaPerPixelInMeters < minMetersPerPixel)
                {
                    minMetersPerPixel = curAreaPerPixelInMeters;
                }
            }

            //convert area in m^2 of 1 pixel to number of pixels in 1 m^2
            if (minMetersPerPixel == double.MaxValue)
            {
                return false;
            }

            pixels = 1.0 / minMetersPerPixel; //return max pixels per square meter

            return true;
        }
    }
}
