using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using OPS.RayTrace;
using OPS.Util;
using Microsoft.Xna.Framework;
using OPS.Imaging;

namespace OPS.Pipeline.Texturing
{
    public class ObsSelectionGreedy : ObsSelectionStrategy
    {
        protected List<Backproject.Context> sortedContexts;
        protected ConcurrentDictionary<string, double> ObsToScore;

        // a strategy that tests a subset of pixels for each observation
        // and sorts them based on the median pixel distance in each
        // sort remains fixed regardless of pt tested in final pass
        // results will choose non-optimal textures in favor of large
        // uninterrupted regions of the same texture. will also show strong
        // seams at mesh tile border as different sorts will be calculated for
        // each tile. can be faster than alternatives depending on percentage of pixels
        // to test (quality).
        public override void Initialize(Mesh mesh, MeshOperator meshOp, SceneCaster occlusionScene, 
                               List<Backproject.Context> allContexts, int outputTextureResolution, double quality,
                               bool writeDebug, string localOutputPath)
        {
            List<PixelPoint> samplePoints = meshOp.SampleUVSpace(outputTextureResolution, outputTextureResolution);

            //intersecting contexts
            ObsToScore = new ConcurrentDictionary<string, double>();
            CoreLimitedParallel.ForEach(allContexts, ctx =>
            {
                var dist = ProjectedPixelDistances.CalculateForObs(occlusionScene, samplePoints,
                                                                   ctx.Obs, ctx.FrustumHull, ctx.ObsToMesh,
                                                                   quality);

                if (dist == double.MaxValue)
                {
                    //if no valid samples, use distance from observation to mesh to have a sortable quality rating
                    //  (that's much bigger than per valid inter-pixel distances), otherwise contexts are not really sorted
                    CameraModel cam = (CameraModel)JsonHelper.FromJson(ctx.Obs.CameraModel);
                    Vector3 cameraInOutput = Vector3.Transform(cam.Unproject(Vector2.Zero).Position, ctx.ObsToMesh); //use arbitrary point (upper-left) to get camera center, for some models this will move center point //bugbug use center?
                    Vector3 meshCenter = meshOp.Bounds.Center();
                    dist = Vector3.Distance(meshCenter, cameraInOutput);
                }

                ObsToScore.AddOrUpdate(ctx.Obs.Name, _ => dist, (_, __) => dist);
            });

            //sort contexts (smaller distance means better quality means sorted to the front of the list)          
            sortedContexts = new List<Backproject.Context>(allContexts);
            sortedContexts.Sort((ctx0, ctx1) => ObsToScore[ctx0.Obs.Name].CompareTo(ObsToScore[ctx1.Obs.Name]));
        }

        public override List<Backproject.Context> FilterAndSortContexts(Vector3 forPoint, List<Backproject.Context> prunedContexts, out ConcurrentDictionary<string, double> scoresByObs)
        {
            scoresByObs = ObsToScore;
            return sortedContexts;
        }
    }
}
