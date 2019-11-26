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
        public double pctSampledPixelsSatisfied;     // of all the pixels sampled for a given source texture, what percentage of them need to be above the split criteria (1.0 any pixel that needs a split is enough, 0.5 at least half the pixels need a split, etc)
        public double subsamplingTriggeringSplit;   // valid values > 1.0. a value of 2.0 would mean if 2 source textures are being squeezed into a single output texture that incur a split

        public int tileResolution;
        public CameraInstance[] cameraInstances;
        public SceneCaster scInMesh;
    }

    public class TextureSplitCriteria : ITileSplitCriteria
    {
        SplitByTextureOpts options;

        public TextureSplitCriteria(SplitByTextureOpts opts)
        {
            options = opts;

            if (opts.pctPixelsToTest <= 0 || opts.pctPixelsToTest > 1)
                throw new Exception("invalid pctPixelsToTest option");

            if(opts.pctSampledPixelsSatisfied <= 0 || opts.pctSampledPixelsSatisfied > 1)
                throw new Exception("invalid pctSampledPixelsServiced option");

            if(opts.subsamplingTriggeringSplit <= 1.0)
                throw new Exception("invalid subsamplingTriggeringSplit option");
        }

        public bool ShouldSplit(MeshOperator meshOperator, BoundingBox bounds)
        {
            Mesh clippedMesh = meshOperator.Clip(bounds);

            // may have too few faces to ever service texture resolution (output atlas too low res)
            if (clippedMesh.Faces.Count == 1)
                return false;

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

            // coarse frustum test: get all observations that intersect mesh hull
            ConvexHull clippedHull = new ConvexHull(clippedMesh);
            List<CameraInstance> intersectingCameras =
                options.cameraInstances.Where(ci => clippedHull.Intersects(ci.hullInMesh)).ToList();

            //no textures would be used on this mesh, no need to split
            if (intersectingCameras.Count == 0)
                return false;
         
            //choose a sub-set of points (for perf) from the output atlas texture to test
            MeshOperator clippedOp =
                new MeshOperator(clippedMesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);
            List<PixelPoint> ptsToTest =
                clippedOp.SubsampleUVSpace(options.pctPixelsToTest, options.tileResolution,  options.tileResolution);

            //record the pixel area of the image that would be used to texture the mesh for each output atlas pixel
            Dictionary<CameraInstance,List<double>> srcAreaByCamera = new Dictionary<CameraInstance, List<double>>();
            foreach (var destPixelPt in ptsToTest)
            {
                //find the camera that provides the best pixel density for this sample
                //(would be the texture we would use at this location)
                if (!GetBestCameraByPixelDensity(intersectingCameras, clippedHull, clippedOp.Bounds, destPixelPt,
                                                 out CameraInstance bestCamera))
                {
                    continue;
                }

                // calculate src pixels area contributing to the pixel  
                Vector2[] pixelCorners = GetPixelCorners(destPixelPt.Pixel); 
                var uvsCorners =
                    pixelCorners.Select(c => Image.PixelToUV(c,options.tileResolution,options.tileResolution));
                var destPixelMeshPositions =
                    uvsCorners
                    .Select(uv => clippedOp.UVToBarycentric(uv))
                    .Where(bary => bary != null)
                    .Select(bary => bary.Position);
                var srcPixels =
                    destPixelMeshPositions
                    .Select(meshPos => GetCameraPixelForMeshPosition(options.scInMesh, bestCamera.cameraModel,
                                                                     bestCamera.cameraToMesh, bestCamera.meshToCamera,
                                                                     bestCamera.hullInMesh, meshPos,
                                                                     bestCamera.widthPixels, bestCamera.heightPixels));

                // if all 4 pixels landed in the source image, find their area in pixels
                if (4 == srcPixels.Where(x => x.HasValue).Count())
                {
                    double srcPixelAreaForDestPixel = CalculatePixelArea(srcPixels.Select(x => x.Value).ToArray());
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
                int idxToTest = (int)((pixelsTested.Count-1) * options.pctSampledPixelsSatisfied);
                if (pixelsTested[idxToTest] >= options.subsamplingTriggeringSplit)
                {
                    return true;
                }
            }

            return false;
        }

        private bool GetBestCameraByPixelDensity(List<CameraInstance> candidateCameras, ConvexHull meshHull,
                                                 BoundingBox meshBounds, PixelPoint pxlPt, out CameraInstance bestCamera)
        {
            double minSpread = double.MaxValue;
            bestCamera = new CameraInstance();
            foreach (var camInst in candidateCameras)
            {
                if (!meshHull.Contains(pxlPt.Point))
                {
                    continue;
                }

                //Issue #523: want median or average in case glancing angle?
                //want a term that looks for consistancy in spacing? implies dead on?
                double curSpread = GetMinPixelSpreadInMeters(options.scInMesh, camInst.cameraModel,
                                                             camInst.cameraToMesh,
                                                             pxlPt.Pixel, pxlPt.Point, meshBounds, 
                                                             camInst.widthPixels, camInst.heightPixels);
                if (curSpread < minSpread)
                {
                    minSpread = curSpread;
                    bestCamera = camInst;
                }
            }

            return minSpread != double.MaxValue;
        }

        private static readonly Vector2[] NeighborPixelsOffsets4Centered =
            {
                new Vector2( -1.0,  0.0),
                new Vector2(  0.0, -1.0),
                new Vector2(  0.0,  1.0),
                new Vector2(  1.0,  0.0)
            };
        
        private static readonly Vector2[] PixelCorners =
            {
                new Vector2(  0.0,  0.0), // upper left
                new Vector2(  0.0,  1.0), // upper right
                new Vector2(  1.0,  1.0), // lower right
                new Vector2(  1.0,  0.0)  // lower left
                
            };

        //method based on TerrainTools GetSourceAreaForRowCol()
        //half the area reported by the cross product is the area of that triangle
        public static double CalculatePixelArea(Vector2[] pixels)
        {
            if (pixels.Length != 4)
            {
                throw new Exception("Need four pixels for area");
            }

            Vector2 ab = (pixels[1] - pixels[0]);
            Vector2 ad = (pixels[3] - pixels[0]);
            Vector2 cb = (pixels[1] - pixels[2]);
            Vector2 cd = (pixels[3] - pixels[2]);

            double area1 = 0.5 * Vector3.Cross(new Vector3(ab, 0), new Vector3(ad, 0)).Length();
            double area2 = 0.5 * Vector3.Cross(new Vector3(cb, 0), new Vector3(cd, 0)).Length();
            return area1 + area2;
        }

        public static Vector2[] GetPixelCorners(Vector2 srcPixel)
        {
            //maps subpixel address to integer pixel address (upper left corner)
            Vector2 pixelAddress = new Vector2((int)srcPixel.X, (int)srcPixel.Y);

            Vector2[] corners = new Vector2[4];
            for (int idxCorner = 0; idxCorner < 4; idxCorner++)
            {
                corners[idxCorner] = pixelAddress + PixelCorners[idxCorner];
            }
            return corners;
        }

        public static List<Vector2> GetOffsetPixels(Vector2 srcPixel, double offset)
        {
            List<Vector2> result = new List<Vector2>();
            for (int idxNeighbor = 0; idxNeighbor < 4; idxNeighbor++)
            {
                result.Add(srcPixel + NeighborPixelsOffsets4Centered[idxNeighbor] * offset);
            }
            return result;
        }
        //Issue #531: raycast bundle of 4 with embree
        //Note: if you are looking through a keyhole at your target point, you could get an overconfident answer of the quality
        // as the corners hit a closer mesh than intended
        public static List<Vector3> GetMeshPositionsForCameraPixels(SceneCaster sceneCaster, CameraModel camera,
                                                                    Matrix camToMesh, BoundingBox specificMeshBounds,
                                                                    IEnumerable<Vector2> srcPixels)
        {
            List<Vector3> result = new List<Vector3>();

            foreach (var curPixel in srcPixels)
            {
                //check if pixel ray hit the mesh
                Vector3? scenePos = Backproject.RaycastMesh(camera, camToMesh, curPixel, sceneCaster);
                if (!scenePos.HasValue)
                    continue;

                //for performance, ignore points whose neighbors spill beyond the mesh of interest
                if (ContainmentType.Contains != specificMeshBounds.Contains(scenePos.Value))
                    continue;

                result.Add(scenePos.Value);
            }

            return result;
        }

        public static Vector2? GetCameraPixelForMeshPosition(SceneCaster sc, CameraModel camera, Matrix camToMesh,
                                                             Matrix meshToCam, ConvexHull camHull,
                                                             Vector3 meshPos, int widthPixels, int heightPixels)
        {
            if (!camHull.Contains(meshPos))
            {
                return null;
            }

            //project into observation
            Vector3 obsPos = Vector3.Transform(meshPos, meshToCam);
            Vector2 obsPixel = camera.Project(obsPos, out double rangeMeshToImage);

            if (rangeMeshToImage <= 0 ||
                (int)obsPixel.X < 0 || (int)obsPixel.X >= widthPixels ||
                (int)obsPixel.Y < 0 || (int)obsPixel.Y >= heightPixels)
            {
                return null; //the center of the pixel may have passed the frustum test, but the pixel corner may not
            }

            // raycast the scene to test if the desired position is occluded by terrain
            if (Backproject.IsOccluded(camera, obsPixel, meshPos, sc, rangeMeshToImage, camToMesh))
            {
                return null;
            }

            return obsPixel;
        }

        //raycast the 4 neighbors of a pixel
        //then measure the distance between the source pixel's intersected position and the neighbors
        //then return the shortest
        //this should give an estimate of the source textures local resolution
        //using our best approximation of the mesh to compare against other images
        public static double GetMinPixelSpreadInMeters(SceneCaster sceneCaster, CameraModel camera, Matrix camToMesh,
                                                       Vector2 srcPixel, Vector3 srcPos, BoundingBox specificMeshBounds,
                                                       int srcWidth, int srcHeight)
        {
            double shortestDistance = double.MaxValue;

            var offsetPixels = GetOffsetPixels(srcPixel, offset: 1.0)
                .Where(px => px.X >= 0 && px.X < srcWidth && px.Y >= 0 && px.Y < srcHeight);
            if (offsetPixels.Count() == 0)
            {
                return double.MaxValue;
            }

            List<Vector3> meshPositions = GetMeshPositionsForCameraPixels(sceneCaster, camera, camToMesh, specificMeshBounds,  offsetPixels);
            foreach (var curPos in meshPositions)
            {
               double sqDist = (curPos - srcPos).LengthSquared();
                if (sqDist < shortestDistance)
                {
                    shortestDistance = sqDist;
                }
            }

            return Math.Sqrt(shortestDistance);
        }
    }
}
