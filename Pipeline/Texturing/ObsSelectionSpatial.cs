using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using OPS.Imaging;
using OPS.RayTrace;
using OPS.Util;
using Microsoft.Xna.Framework;
using System.IO;

namespace OPS.Pipeline.Texturing
{
    // a strategy that samples the mesh at a fixed distribution on the surface
    // exhaustive results are calculated for each sampling point. when final sortings are 
    // performed they apply a weighting to each of the sampling results based on the 
    // sample points distance from the current point. the goal is higher fidelity than greedy
    // and tunable noisiness that is better than exhaustive. 
    public class ObsSelectionSpatial : ObsSelectionStrategy
    {
        public override ObsSelectionStrategyName Name { get { return ObsSelectionStrategyName.Spatial; } }

        Dictionary<string, Backproject.Context> ObsToContext = new Dictionary<string, Backproject.Context>();

        Dictionary<string, List<ScoredPoint>> ScoredRefPtsByObs = new Dictionary<string, List<ScoredPoint>>();

        public override void Initialize(Mesh mesh, MeshOperator meshOp, SceneCaster occlusionScene,
                                        List<Backproject.Context> contexts, int outputTextureResolution,
                                        double quality = 1)
        {
            // any sorts that would be better served by orbital will have their contexts filtered

            // collect points on the surface of the mesh
            double samplesPerMeter = quality * 100.0;

            //TODO why can't this just be new SurfacePointSampler().Sample()?
            Mesh sampledMesh = new SurfacePointSampler().GenerateSampledMesh(mesh, samplesPerMeter);

            //guarantee at least a single point on the mesh (zero on mesh can occur with very small meshes)
            while (sampledMesh.Vertices.Count == 0)
            {
                samplesPerMeter += 1;
                sampledMesh = new SurfacePointSampler().GenerateSampledMesh(mesh, samplesPerMeter);
            }

            if (!string.IsNullOrEmpty(DebugOutputPath))
            {
                mesh.Save(PathHelper.EnsureDir(DebugOutputPath, "sceneMesh.ply"));
                sampledMesh.Save(PathHelper.EnsureDir(DebugOutputPath, "spatialSamplePts_base.ply"));
            }

            //add center point of each observation (to make sure small fov images are considered)
            foreach (var ctx in contexts)
            {
                Vector2 pixel = new Vector2(ctx.Obs.Width / 2.0, ctx.Obs.Height / 2.0);
                Vector3? res = Backproject.RaycastMesh(ctx.CameraModel, ctx.ObsToMesh, pixel, occlusionScene);
                if (res.HasValue)
                {
                    sampledMesh.Vertices.Add(new Vertex(res.Value));
                }
            }

            if (!string.IsNullOrEmpty(DebugOutputPath))
            {
                sampledMesh.Save(PathHelper.EnsureDir(DebugOutputPath, "spatialSamplePts_wObs.ply"));
            }

            //calculate the scores per reference point (grouped by observation)
            var scoredRefPtsByObs = new Dictionary<string, ConcurrentBag<ObsSelectionStrategy.ScoredPoint>>();
            foreach (var ctx in contexts)
            {
                scoredRefPtsByObs.Add(ctx.Obs.Name, new ConcurrentBag<ObsSelectionStrategy.ScoredPoint>());
                ObsToContext.Add(ctx.Obs.Name, ctx);
            }

            //exhaustively sort for each sample point
            var refSelect = new ObsSelectionExhaustive();
            refSelect.OrbitalMetersPerPixel = OrbitalMetersPerPixel;
            refSelect.Initialize(mesh, meshOp, occlusionScene, contexts, outputTextureResolution, quality);

            //collect a sorted list of contexts (best to worst) for each sample point
            CoreLimitedParallel.ForEach(sampledMesh.Vertices.Select(v => v.Position), pt =>
            {
                Dictionary<string, double> ptScoresByObs = new Dictionary<string, double>();

                var sortedContexts = refSelect.FilterAndSortContexts(pt, contexts, ptScoresByObs);

                foreach (var pair in ptScoresByObs)
                {
                    scoredRefPtsByObs[pair.Key].Add(new ObsSelectionStrategy.ScoredPoint(pt, pair.Value));
                }

                if (!string.IsNullOrEmpty(DebugOutputPath) && sortedContexts.Count() > 0)
                {
                    using (StreamWriter sw =
                           new StreamWriter(PathHelper.EnsureDir(DebugOutputPath,
                                                                 $"RefScoresForPoint_{pt.X}_{pt.Y}_{pt.Z}.txt")))
                    {
                        sw.WriteLine(string.Format("{0}: {1}", "Observation Name", "Score (lower is better)"));
                        foreach (var ctx in sortedContexts)
                        {
                            sw.WriteLine(string.Format("{0}: {1}", ctx.Obs.Name, ptScoresByObs[ctx.Obs.Name]));
                        }
                    }
                }
            });

            //flatten to list for later perf
            foreach (var ctx in contexts)
            {
                ScoredRefPtsByObs.Add(ctx.Obs.Name, scoredRefPtsByObs[ctx.Obs.Name].ToList());
            }
        }

        public override List<Backproject.Context> FilterAndSortContexts(Vector3 forPoint,
                                                                        List<Backproject.Context> contexts,
                                                                        Dictionary<string, double> scoresByObs = null)
        {
            var sortedContexts = new List<Backproject.Context>(contexts.Count);
            var scoresByObsIndex = new Dictionary<int, double>(contexts.Count);

            foreach (var ctx in contexts)
            {
                if (!ObsToContext.ContainsKey(ctx.Obs.Name))
                {
                    throw new Exception("Unexpected context as compared to init: " + ctx.Obs.Name);
                }

                //early out if context has no chance for pt
                if (ctx.FrustumHull.Contains(forPoint))
                {
                    double minWeightedScore = double.MaxValue;
                   
                    foreach (var pt in ScoredRefPtsByObs[ctx.Obs.Name])
                    {
                        if (pt.Score == double.MaxValue)
                        {
                            throw new Exception("invalid score provided");
                        }

                        //heuristic: makes a quality metric from the min pixel spread on the terrain
                        //and the squared distance to ther reference pt
                        double distanceToRefPtSq = Vector3.DistanceSquared(pt.Point, forPoint);
                        double weightedScore = distanceToRefPtSq * pt.Score;
                        if (weightedScore < minWeightedScore)
                        {
                            minWeightedScore = weightedScore;
                        }
                    }
                    
                    if (minWeightedScore != double.MaxValue)
                    {
                        scoresByObsIndex.Add(ctx.Obs.Index, minWeightedScore);
                        sortedContexts.Add(ctx);
                    }
                    
                }
            }

            sortedContexts
                .Sort((ctx0, ctx1) => scoresByObsIndex[ctx0.Obs.Index].CompareTo(scoresByObsIndex[ctx1.Obs.Index]));

            //optionally return scores
            if (scoresByObs != null)
            {
                scoresByObs.Clear();

                foreach (var ctx in sortedContexts)
                {
                    scoresByObs.Add(ctx.Obs.Name, scoresByObsIndex[ctx.Obs.Index]);
                }
            }

            return sortedContexts;
        }
    }
}
