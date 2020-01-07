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
        Dictionary<string, Backproject.Context> ObsToContext;
        Dictionary<string, List<ObsSelectionStrategy.ScoredPoint>> ScoredRefPtsByObs;

        public override void Initialize(Mesh mesh, MeshOperator meshOp, SceneCaster occlusionScene,
                               List<Backproject.Context> allContexts, int outputTextureResolution, double quality,
                               bool writeDebug, string localOutputPath)
        {
            // collect points on the surface of the mesh
            double samplesPerMeter = quality * 100.0;
            Mesh sampledMesh = new SurfacePointSampler().GenerateSampledMesh(mesh, samplesPerMeter);

            //guarantee at least a single point on the mesh (zero on mesh can occur with very small meshes)
            while (sampledMesh.Vertices.Count == 0)
            {
                samplesPerMeter += 1;
                sampledMesh = new SurfacePointSampler().GenerateSampledMesh(mesh, samplesPerMeter);
            }

            if (writeDebug)
            {
                mesh.Save(Path.Combine(localOutputPath, "sceneMesh.ply"));
                sampledMesh.Save(Path.Combine(localOutputPath, "spatialSamplePts_base.ply"));
            }

            //add center point of each observation (to make sure small fov images are considered)
            foreach (var ctx in allContexts)
            {
                Vector2 pixel = new Vector2(ctx.Obs.Width / 2.0, ctx.Obs.Height / 2.0);
                Vector3? res = Backproject.RaycastMesh(ctx.CameraModel, ctx.ObsToMesh, pixel, occlusionScene);
                if (res.HasValue)
                {
                    sampledMesh.Vertices.Add(new Vertex(res.Value));
                }
            }

            if (writeDebug)
            {
                sampledMesh.Save(Path.Combine(localOutputPath, "spatialSamplePts_wObs.ply"));
            }

            //calculate the scores per reference point (grouped by observation)
            ObsToContext = new Dictionary<string, Backproject.Context>();
            Dictionary<string, ConcurrentBag<ObsSelectionStrategy.ScoredPoint>> scoredRefPtsByObs = new Dictionary<string, ConcurrentBag<ObsSelectionStrategy.ScoredPoint>>();
            foreach (var ctx in allContexts)
            {
                scoredRefPtsByObs.Add(ctx.Obs.Name, new ConcurrentBag<ObsSelectionStrategy.ScoredPoint>());
                ObsToContext.Add(ctx.Obs.Name, ctx);
            }

            string ptDebugPath = localOutputPath;

            //collect a sorted list of contexts (best to worst) for each sample point
            CoreLimitedParallel.ForEach(sampledMesh.Vertices.Select(v => v.Position), pt =>
            {
                if (writeDebug)
                {
                    ptDebugPath = Path.Combine(localOutputPath, "Point_" + pt.X + "_" + pt.Y + "_" + pt.Z);
                }

                //exhaustively sort for each sample point
                ObsSelectionExhaustive refSelect = new ObsSelectionExhaustive();
                refSelect.Initialize(mesh, meshOp, occlusionScene, allContexts, outputTextureResolution, quality, writeDebug, ptDebugPath);
                Dictionary<string, double> ptScoresByObs = new Dictionary<string, double>();

                List<Backproject.Context> sortedContexts = new List<Backproject.Context>(allContexts.Count());
                refSelect.FilterAndSortContexts(pt, allContexts, sortedContexts, ptScoresByObs);

                foreach (var pair in ptScoresByObs)
                {
                    scoredRefPtsByObs[pair.Key].Add(new ObsSelectionStrategy.ScoredPoint(pt, pair.Value));
                }

                if (writeDebug && sortedContexts.Count() > 0)
                {
                    using (StreamWriter sw = new StreamWriter(Path.Combine(localOutputPath, "RefScoresForPoint_" + pt.X + "_" + pt.Y + "_" + pt.Z + ".txt")))
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
            ScoredRefPtsByObs = new Dictionary<string, List<ScoredPoint>>();
            foreach (var ctx in allContexts)
            {
                ScoredRefPtsByObs.Add(ctx.Obs.Name, scoredRefPtsByObs[ctx.Obs.Name].ToList());
            }
        }

        public override void FilterAndSortContexts(Vector3 forPoint, List<Backproject.Context> inContexts, List<Backproject.Context> sortedContexts, Dictionary<string, double> scoresByObs)
        {
            Dictionary<int, double> scoresByObsIndex = new Dictionary<int, double>(inContexts.Count());

            foreach (var ctx in inContexts)
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
                        //heuristic: assigns equal value to distance from sample point and the min pixel spread on the terrain
                        double distSq = Vector3.DistanceSquared(pt.Point, forPoint);
                        if(pt.Score == double.MaxValue)
                        {
                            continue;
                        }

                        double weightedScore = distSq * pt.Score;
                        if (weightedScore < minWeightedScore)
                        {
                            minWeightedScore = weightedScore;
                        }
                    }

                    if (!scoresByObsIndex.ContainsKey(ctx.Obs.Index))
                    {
                        scoresByObsIndex.Add(ctx.Obs.Index, minWeightedScore);
                        sortedContexts.Add(ctx);
                    }
                    else
                    {
                        scoresByObsIndex[ctx.Obs.Index] = minWeightedScore;
                    }
                }
            }

            sortedContexts.Sort((ctx0, ctx1) => scoresByObsIndex[ctx0.Obs.Index].CompareTo(scoresByObsIndex[ctx1.Obs.Index]));

            //optionally return scores
            if (scoresByObs != null)
            {
                foreach (var ctx in sortedContexts)
                {
                    scoresByObs.Add(ctx.Obs.Name, scoresByObsIndex[ctx.Obs.Index]);
                }
            }
        }
    }
}
