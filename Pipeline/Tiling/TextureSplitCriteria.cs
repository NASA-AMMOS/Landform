using System;
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
    public struct CameraInstance
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
                }
                catch
                {
                    //TODO: not being able to atlas can be caused by mesh complexity, which might be helped by a split
                    //returning false in case there's a mesh that wont atlas (degenerate triangles?) this would recurse down to single triangle tiles                    
                    return false;
                }
            }

            // coarse frustum test: get all observations that intersect mesh hull
            ConvexHull clippedHull = new ConvexHull(clippedMesh);
            List<CameraInstance> intersectingCameras = options.cameraInstances.Where(ci => clippedHull.Intersects(ci.hullInMesh)).ToList();

            //no textures would be used on this mesh, no need to split
            if (intersectingCameras.Count == 0)
                return false;
         
            //choose a sub-set of points (for perf) from the output atlas texture to test
            MeshOperator clippedOp = new MeshOperator(clippedMesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);
            List<PixelPoint> ptsToTest = clippedOp.SubsampleUVSpace(options.pctPixelsToTest, options.tileResolution,  options.tileResolution);

            //record the pixel area of the image that would be used to texture the mesh for each output atlas pixel
            Dictionary<CameraInstance,List<double>> srcAreaByCamera = new Dictionary<CameraInstance, List<double>>();
            foreach (var destPixelPt in ptsToTest)
            {
                //find the camera that provides the best pixel density for this sample (would be the texture we would use at this location)
                if (!GetBestCameraByPixelDensity(intersectingCameras, clippedHull, destPixelPt, out CameraInstance bestCamera))
                    continue;

                // calculate src pixels area contributing to the pixel  
                Vector2[] pixelCorners = OPS.Pipeline.LocalBuildMeshes.GetPixelCorners(destPixelPt.Pixel);                
                var uvsCorners = pixelCorners.Select(c => Image.PixelToUV(c,options.tileResolution,options.tileResolution));
                var destPixelMeshPositions = uvsCorners.Select(uv => clippedOp.UVToBarycentric(uv)).Where(bary => bary != null).Select(bary => bary.Position);
                var srcPixels = destPixelMeshPositions.Select(meshPos => LocalBuildMeshes.GetCameraPixelForMeshPosition(options.scInMesh, bestCamera.cameraModel, bestCamera.cameraToMesh, bestCamera.meshToCamera, bestCamera.hullInMesh, meshPos, bestCamera.widthPixels, bestCamera.heightPixels));

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

            //these area values represent the number of pixels in the src textures being squished or streched to fill the destination texture pixels
            // ideally we would like that number to be 1, but we are at the mercy of the uvatlas which can choose to compress an areas texture sampling based solely on geometry.
            // if the area is greater than 1 at the percentage of pixels requested we should subdivide and try again with the new leaf tile
            foreach (var key in srcAreaByCamera.Keys)
            {
                var pixelsTested = srcAreaByCamera[key];
                if (pixelsTested == null)
                    continue;

                // is current atlas fine for texture resolution
                if (!pixelsTested.Any(x => x > 1.0))
                    continue;

                //the option specifies the percentage of pixels that need to be satisfied to avoid a split           
                pixelsTested.Sort();
                int idxToTest = (int)((pixelsTested.Count-1) * options.pctSampledPixelsSatisfied);
                if (pixelsTested[idxToTest] >= options.subsamplingTriggeringSplit)
                    return true;
            }

            return false;
        }

        private bool GetBestCameraByPixelDensity(List<CameraInstance> candidateCameras, ConvexHull meshHull, PixelPoint pxlPt, out CameraInstance bestCamera)
        {
            double minSpread = double.MaxValue;
            bestCamera = new CameraInstance();
            foreach (var camInst in candidateCameras)
            {
                if (!meshHull.Contains(pxlPt.Point))
                    continue;

                //Issue #523: want median or average in case glancing angle? want a term that looks for consistancy in spacing? implies dead on?
                double curSpread = OPS.Pipeline.LocalBuildMeshes.GetMinPixelSpreadInMeters(options.scInMesh, camInst.cameraModel, camInst.cameraToMesh, meshHull, pxlPt.Pixel, pxlPt.Point, camInst.widthPixels, camInst.heightPixels);
                if (curSpread < minSpread)
                {
                    minSpread = curSpread;
                    bestCamera = camInst;
                }
            }

            return minSpread != double.MaxValue;
        }


        //method based on TerrainTools GetSourceAreaForRowCol (half the area reported by the cross product is the area of that triangle)
        public double CalculatePixelArea(Vector2[] pixels)
        {
            if (pixels.Length != 4)
                throw new Exception("Need four pixels for area");

            Vector2 ab = (pixels[1] - pixels[0]);
            Vector2 ad = (pixels[3] - pixels[0]);
            Vector2 cb = (pixels[1] - pixels[2]);
            Vector2 cd = (pixels[3] - pixels[2]);

            double area1 = 0.5 * Vector3.Cross(new Vector3(ab, 0), new Vector3(ad, 0)).Length();
            double area2 = 0.5 * Vector3.Cross(new Vector3(cb, 0), new Vector3(cd, 0)).Length();
            return area1 + area2;
        }
    }

}