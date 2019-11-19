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

        public override void Initialize(Mesh mesh, ConvexHull meshHull, MeshOperator meshOp, SceneCaster occlusionScene,
                               List<Backproject.Context> contexts, int outputTextureResolution, double quality)
        {
            MeshHull = meshHull;
            OcclusionScene = occlusionScene;
            Contexts = contexts;
        }

        public override List<Backproject.Context> SortContexts(PixelPoint forPixel, out ConcurrentDictionary<string, double> scoresByObs)
        {
            //calculate goodness: median distance between neighboring source pixels in meters on the terrain
            //smaller distance == better texture resolution
            var localScoresByObs = new ConcurrentDictionary<string, double>();
            Serial.ForEach(Contexts, ctx =>
            {
                double dist = ProjectedPixelDistances.CalculateForObs(OcclusionScene, MeshHull, new List<PixelPoint>() { forPixel },
                                                                       ctx.Obs, ctx.FrustumHull, ctx.ObsToMesh);
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
            List<Backproject.Context> sortedContexts = new List<Backproject.Context>(Contexts);
            sortedContexts.Sort((ctx0, ctx1) => localScoresByObs[ctx0.Obs.Name].CompareTo(localScoresByObs[ctx1.Obs.Name]));
            scoresByObs = new ConcurrentDictionary<string, double>(localScoresByObs);

            return sortedContexts;
        }
    }
}
