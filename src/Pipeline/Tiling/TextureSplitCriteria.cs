using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.Geometry;
using JPLOPS.Imaging;
using JPLOPS.RayTrace;

namespace JPLOPS.Pipeline
{
    public class CameraInstance
    {
        public Matrix CameraToMesh;
        public Matrix MeshToCamera;
        public CameraModel CameraModel;
        public ConvexHull HullInMesh;
        public int WidthPixels;
        public int HeightPixels;
    };

    public class TextureSplitOptions
    {
        // if the tile texture already would max out MaxTileResolution to reach MaxTexelsPerMeter then don't split
        public bool RespectMaxTexelsPerMeter;

        // how densely should the destination texels be sampled
        public double PercentPixelsToTest = TilingDefaults.TEX_SPLIT_PERCENT_TO_TEST;

        // of all the pixels sampled for a given source texture, what percentage of them need to suggest a split
        public double PercentPixelsSatisfied = TilingDefaults.TEX_SPLIT_PERCENT_SATISFIED;

        // valid values >= 1; 2 would mean if > 2 source pixels are squeezed into a single output texel then split
        public double MaxPixelsPerTexel = TilingDefaults.TEX_SPLIT_MAX_PIXELS_PER_TEXEL;

        public int MaxTileResolution = TilingDefaults.MAX_TILE_RESOLUTION;
        public int MinTileResolution = TilingDefaults.MIN_TILE_RESOLUTION;
        public double MaxTexelsPerMeter = TilingDefaults.MAX_TEXELS_PER_METER;
        public double MaxOrbitalTexelsPerMeter = TilingDefaults.MAX_ORBITAL_TEXELS_PER_METER;
        public double MaxTextureStretch = TilingDefaults.MAX_TEXTURE_STRETCH;
        public bool PowerOfTwoTextures = TilingDefaults.POWER_OF_TWO_TEXTURES;

        public TextureMode TextureMode = TilingDefaults.TEXTURE_MODE;

        public CameraInstance[] CameraInstances;

        public SceneCaster SceneCaster;

        public BoundingBox? SurfaceBounds;

        public double RaycastTolerance;

        public bool RedoUVs;
        public Action<Mesh, BoundingBox, int> AtlasTile;

        public Action<string> Warn;
    }

    abstract public class TextureSplitCriteria : TileSplitCriteria
    {
        public readonly TextureSplitOptions options;

        protected bool spewProgress;

        public TextureSplitCriteria(TextureSplitOptions opts)
        {
            options = opts;

            if (opts.PercentPixelsToTest <= 0 || opts.PercentPixelsToTest > 1)
            {
                throw new Exception("invalid PercentPixelsToTest option");
            }

            if (opts.PercentPixelsSatisfied <= 0 || opts.PercentPixelsSatisfied > 1)
            {
                throw new Exception("invalid PercentPixelsSatisfied option");
            }

            if (opts.MaxPixelsPerTexel < 1)
            {
                throw new Exception("invalid MaxPixelsPerTexel option");
            }
        }

        public string ShouldSplit(BoundingBox bounds, params MeshOperator[] meshOps)
        {
            //TODO ISSUE 1038:  add the resolution of orbital to this decision

            if (meshOps.Length != 1)
            {
                throw new ArgumentException("TextureSplitCriteria can only operate on a single mesh");
            }
            var meshOperator = meshOps[0];

            // coarse frustum test against the bounding box
            var intersectingCameras = options.CameraInstances
                .Where(ci => ci.HullInMesh != null && ci.HullInMesh.Intersects(bounds))
                .ToList();
            if (intersectingCameras.Count == 0)
            {
                return null;
            }

            Mesh clippedMesh = meshOperator.Clipped(bounds);
            if (!clippedMesh.HasFaces)
            {
                return null;
            }

            // finer frustum test: get all observations that intersect mesh hull
            ConvexHull clippedHull = ConvexHull.Create(clippedMesh);
            intersectingCameras = intersectingCameras.Where(ci => clippedHull.Intersects(ci.HullInMesh)).ToList();

            //no textures would be used on this mesh, no need to split
            if (intersectingCameras.Count == 0)
            {
                return null;
            }

            double meshArea = clippedMesh.SurfaceArea();
            bool orbital = options.SurfaceBounds.HasValue &&
                options.SurfaceBounds.Value.Contains(bounds) == ContainmentType.Disjoint;
            double texelsPerMeter = orbital ? options.MaxOrbitalTexelsPerMeter : options.MaxTexelsPerMeter;
            int texRes = SceneNodeTilingExtensions.
                GetTileResolution(meshArea, options.MaxTileResolution, options.MinTileResolution, texelsPerMeter,
                                  options.PowerOfTwoTextures);

            if (options.RespectMaxTexelsPerMeter && texRes < options.MaxTileResolution)
            {
                return null;
            }

            //for a representative texel estimate the ratio of observation pixel area to tile texel area
            //different implementations may have different definitions of "area"
            //but it doesn't matter because we're just going to take a ratio, so that will divide out
            if (!GetTileTexelsPerArea(bounds, clippedMesh, texRes, meshArea, out double texels))
            {
                return null;
            }

            if (!GetObservationPixelsPerArea(bounds, clippedMesh, texRes, clippedHull, intersectingCameras,
                                             out double pixels))
            {
                return null;
            }

            double ppt = pixels / texels;

            return ppt > options.MaxPixelsPerTexel ? $"{ppt:f3} > {options.MaxPixelsPerTexel:f3} pixels/texel" : null;
        }

        protected abstract bool GetObservationPixelsPerArea(BoundingBox bounds, Mesh clippedMesh, int texRes,
                                                            ConvexHull clippedHull,
                                                            List<CameraInstance> intersectingCameras,
                                                            out double pixels);

        protected abstract bool GetTileTexelsPerArea(BoundingBox bounds, Mesh clippedMesh, int texRes, double meshArea,
                                                     out double texels);
    }

    public class TextureSplitCriteriaBackproject : TextureSplitCriteria
    {
        public TextureSplitCriteriaBackproject(TextureSplitOptions opts) : base(opts)
        {
            spewProgress = true;
        }

        protected override bool GetTileTexelsPerArea(BoundingBox bounds, Mesh clippedMesh, int texRes, double meshArea,
                                                     out double texels)
        {
            texels = 1; //this implementation always computes observation pixel area for one tile texel
            return true;
        }

        protected override bool GetObservationPixelsPerArea(BoundingBox bounds, Mesh clippedMesh, int texRes,
                                                            ConvexHull clippedHull,
                                                            List<CameraInstance> intersectingCameras, out double pixels)
        {
            pixels = 0;

            if (!clippedMesh.HasUVs || options.RedoUVs)
            {
                if (options.CameraInstances.Length == 1)
                {
                    var ci = options.CameraInstances[0];
                    clippedMesh.ProjectTexture(ci.WidthPixels, ci.HeightPixels, ci.CameraModel, ci.MeshToCamera);
                    if (!clippedMesh.HasFaces)
                    {
                        return false;
                    }
                    if (options.TextureMode != TextureMode.Clip)
                    {
                        clippedMesh.RescaleUVsForTexture(texRes, texRes, options.MaxTextureStretch);
                    }
                }
                else
                {
                    if (options.AtlasTile != null)
                    {
                        options.AtlasTile(clippedMesh, bounds, texRes);
                    }
                    else
                    {
                        if (!UVAtlas.Atlas(clippedMesh, texRes, texRes, maxStretch: options.MaxTextureStretch,
                                           logger: new ThunkLogger() { Warn = options.Warn }))
                        {
                            //TODO: atlas fail can be caused by mesh complexity, which might be helped by a split 
                            //returning false in case there's a mesh that wont atlas (degenerate triangles?)
                            //this would recurse down to single triangle tiles
                            return false;
                        }
                    }
                }
            }

            //choose a sub-set of points (for perf) from the output atlas texture to test
            MeshOperator clippedOp =
                new MeshOperator(clippedMesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);
            List<PixelPoint> ptsToTest = clippedOp.SubsampleUVSpace(options.PercentPixelsToTest, texRes, texRes);

            var clippedCaster = new SceneCaster();
            clippedCaster.AddMesh(clippedMesh, null, Matrix.Identity);
            clippedCaster.Build();

            //record the pixel area of the image that would be used to texture the mesh for each output atlas pixel
            var srcAreaByCamera = new Dictionary<CameraInstance, List<double>>();
            foreach (var destPixelPt in ptsToTest)
            { 
                //if the points are spilling onto other tiles, they aren't great candidates for testing.
                //In addition to handling cases where you are peeking through a valley or keyhole in the terrain
                //and all points are landing on other mesh tiles, this is a performance optimization.
                if (!clippedHull.Contains(destPixelPt.Point, TilingDefaults.MESH_HULL_TEST_EPSILON))
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
                            GetCameraPixelForMeshPosition(options.SceneCaster, bestCamera.CameraModel,
                                                          bestCamera.CameraToMesh, bestCamera.MeshToCamera,
                                                          bestCamera.HullInMesh, meshPos,
                                                          bestCamera.WidthPixels, bestCamera.HeightPixels,
                                                          options.RaycastTolerance))
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
                int idxToTest = (int)((srcPixelAreas.Count - 1) * options.PercentPixelsSatisfied);

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
                    GetCameraPixelForMeshPosition(options.SceneCaster, camInst.CameraModel, camInst.CameraToMesh,
                                                  camInst.MeshToCamera, camInst.HullInMesh,
                                                  pxlPt.Point, camInst.WidthPixels, camInst.HeightPixels,
                                                  options.RaycastTolerance);

                if (!srcPixel.HasValue)
                {
                    continue;
                }

                //Issue #523: want median or average in case glancing angle?
                //want a term that looks for consistancy in spacing? implies dead on?
                double curSpread = ProjectedPixelDistances.
                    GetPixelSpreadInMeters(meshBounds, meshCaster, options.SceneCaster, camInst.CameraModel,
                                           camInst.CameraToMesh, srcPixel.Value, pxlPt.Point,
                                           camInst.WidthPixels, camInst.HeightPixels, options.RaycastTolerance);
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
        public TextureSplitCriteriaApproximate(TextureSplitOptions opts) : base(opts) {}

        protected override bool GetTileTexelsPerArea(BoundingBox bounds, Mesh clippedMesh, int texRes, double meshArea,
                                                     out double texels)
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
            texels = TilingDefaults.APPROX_TEXTURE_UTILIZATION * numTexels / meshArea; //return texels per square meter

            return true;
        }

        protected override bool GetObservationPixelsPerArea(BoundingBox bounds, Mesh clippedMesh, int texRes,
                                                            ConvexHull clippedHull,
                                                            List<CameraInstance> intersectingCameras, out double pixels)
        {
            pixels = 0;

            double minMetersPerPixel = double.MaxValue;
            foreach (var camInst in intersectingCameras)
            {
                // project grid of 25 central rays from camera to estimate closest distance to mesh
                var samples = new List<Vector2>(25);
                int rowSkip = camInst.HeightPixels / 6;
                int colSkip = camInst.WidthPixels / 6;
                for (int r = rowSkip; r < camInst.HeightPixels; r += rowSkip)
                {
                    for (int c = colSkip; c < camInst.HeightPixels; c += colSkip)
                    {
                        samples.Add(new Vector2(c, r));
                    }
                }

                var pts = ProjectedPixelDistances
                    .GetMeshPositionsForCameraPixels(bounds, options.SceneCaster, options.SceneCaster,
                                                     camInst.CameraModel, camInst.CameraToMesh, samples,
                                                     options.RaycastTolerance);
                if (pts.Count < 1)
                {
                    continue;
                }

                Vector3 camInMesh = Vector3.Transform(((CAHV)camInst.CameraModel).C, camInst.CameraToMesh);
                double minDist = pts.Min(pt => Vector3.Distance(camInMesh, pt));

                // use a square pixel approximation
                Vector2 []corners = Image.GetPixelCorners(new Vector2(camInst.WidthPixels / 2.0,
                                                                      camInst.HeightPixels / 2.0));
                try
                {
                    Vector3 ptUpperLeftCorner = camInst.CameraModel.Unproject(corners[0], minDist);
                    Vector3 ptUpperRightCorner = camInst.CameraModel.Unproject(corners[1], minDist);
                    Vector3 ptLowerRightCorner = camInst.CameraModel.Unproject(corners[2], minDist);
                    Vector3 ptLowerLeftCorner = camInst.CameraModel.Unproject(corners[3], minDist);
                    double projectedWidth = Vector3.Distance(ptUpperLeftCorner, ptUpperRightCorner);
                    double projectedHeight = Vector3.Distance(ptUpperLeftCorner, ptLowerLeftCorner);
                    double curAreaPerPixelInMeters = projectedWidth * projectedHeight;
                    if (curAreaPerPixelInMeters < minMetersPerPixel)
                    {
                        minMetersPerPixel = curAreaPerPixelInMeters;
                    }
                }
                catch (CameraModelException) {}
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
