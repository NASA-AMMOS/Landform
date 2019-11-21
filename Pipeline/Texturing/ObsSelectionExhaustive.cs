using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;
using OPS.RayTrace;
using OPS.Util;
using Microsoft.Xna.Framework;
using OPS.Imaging;
using System.IO;

namespace OPS.Pipeline.Texturing
{
    // a strategy that tests all observations for each pixel
    // will return the highest quality pixel for every output
    // pixel but can create a noisy result by pingponging
    // between similar quality textures
    class ObsSelectionExhaustive : ObsSelectionStrategy
    {
        protected ConvexHull MeshHull;
        protected SceneCaster OcclusionScene;

        protected List<Backproject.Context> Contexts;
        protected bool WriteDebug;
        protected string LocalOutputPath;

        public override void Initialize(Mesh mesh, ConvexHull meshHull, MeshOperator meshOp, SceneCaster occlusionScene, 
                               List<Backproject.Context> contexts, int outputTextureResolution, double quality, bool writeDebug, string localOutputPath)
        {
            MeshHull = meshHull;

            OcclusionScene = occlusionScene;
            Contexts = contexts;
            WriteDebug = writeDebug;
            LocalOutputPath = localOutputPath;
        }

        public override List<Backproject.Context> FilterAndSortContexts(PixelPoint forPixel, out ConcurrentDictionary<string, double> scoresByObs)
        {
            string localOutputPathPixel = null;
            if (WriteDebug)
            {
                localOutputPathPixel = Path.Combine(LocalOutputPath, (int)forPixel.Pixel.X + "_" + (int)forPixel.Pixel.Y);
                PathHelper.EnsureExists(localOutputPathPixel);
            }

            //intersecting contexts
            var visibleContexts = new List<Backproject.Context>();
            Serial.ForEach(Contexts, ctx =>
            {
                if (ctx.FrustumHull.Contains(forPixel.Point))
                {
                    visibleContexts.Add(ctx);
                }
            });

            //calculate goodness: median distance between neighboring source pixels in meters on the terrain
            //smaller distance == better texture resolution
            var localScoresByObs = new ConcurrentDictionary<string, double>();
            Serial.ForEach(visibleContexts, ctx =>
            {
                double dist = ProjectedPixelDistances.CalculateForObs(OcclusionScene, new List<PixelPoint>() { forPixel },
                                                                       ctx.Obs, ctx.FrustumHull, ctx.ObsToMesh, 1.0, WriteDebug, localOutputPathPixel);
                if(dist == double.MaxValue)
                {
                    //if no valid samples, use distance from observation to mesh to have a sortable quality rating
                    //  (that's much bigger than per valid inter-pixel distances), otherwise contexts are not really sorted
                    //TODO: try if all the same value even if valid distances? tie-breaker?
                    CameraModel cam = (CameraModel)JsonHelper.FromJson(ctx.Obs.CameraModel);
                    Vector3 cameraInOutput = Vector3.Transform(cam.Unproject(forPixel.Pixel).Position, ctx.ObsToMesh);
                    Vector3 meshCenter = MeshHull.Mesh.Bounds().Center();
                    dist = Vector3.Distance(meshCenter, cameraInOutput);
                }

                localScoresByObs.AddOrUpdate(ctx.Obs.Name, _ => dist, (_, __) => dist);
            });

            //sort contexts by decreasing quality
            List<Backproject.Context> sortedContexts = new List<Backproject.Context>(visibleContexts);
            sortedContexts.Sort((ctx0, ctx1) => localScoresByObs[ctx0.Obs.Name].CompareTo(localScoresByObs[ctx1.Obs.Name]));
            scoresByObs = new ConcurrentDictionary<string, double>(localScoresByObs);

            if (WriteDebug)
            {            
                using (StreamWriter sw = new StreamWriter(Path.Combine(localOutputPathPixel, "Scores.txt")))
                {
                    foreach (var ctx in sortedContexts)
                    {
                        sw.WriteLine(string.Format("{0}: {1}", ctx.Obs.Name, localScoresByObs[ctx.Obs.Name]));
                    }
                }
            }

            return sortedContexts;
        }
    }
}
