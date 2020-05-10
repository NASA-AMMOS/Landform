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

        private double samplesPerMeter;

        private Dictionary<string, List<ScoredPoint>> ScoredRefPtsByObs = new Dictionary<string, List<ScoredPoint>>();

        private struct ScoredPoint
        {
            public Vector3 Point;
            public double Score;

            public ScoredPoint(Vector3 point, double score)
            {
                Point = point;
                Score = score;
            }
        }

        public override void Initialize(Mesh mesh, MeshOperator meshOp, SceneCaster occlusionScene,
                                        List<Backproject.Context> contexts, int outputTextureResolution,
                                        double quality = 1)
        {
            // any sorts that would be better served by orbital will have their contexts filtered

            // collect points on the surface of the mesh
            samplesPerMeter = quality * 100.0;

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
            var scoredRefPtsByObs = new Dictionary<string, ConcurrentBag<ScoredPoint>>();
            foreach (var ctx in contexts)
            {
                scoredRefPtsByObs.Add(ctx.Obs.Name, new ConcurrentBag<ScoredPoint>());
            }

            //exhaustively sort for each sample point
            var refSelect = new ObsSelectionExhaustive();
            refSelect.OrbitalMetersPerPixel = OrbitalMetersPerPixel;
            refSelect.Initialize(mesh, meshOp, occlusionScene, contexts, outputTextureResolution, quality);

            //collect a sorted list of contexts (best to worst) for each sample point
            CoreLimitedParallel.ForEach(sampledMesh.Vertices, vertex =>
            {
                var pt = vertex.Position;
                var ptScoresByObs = new Dictionary<string, double>();
                var sortedContexts = refSelect.FilterAndSortContexts(pt, contexts, ptScoresByObs);

                foreach (var ctx in sortedContexts)
                {
                    scoredRefPtsByObs[ctx.Obs.Name].Add(new ScoredPoint(pt, ptScoresByObs[ctx.Obs.Name]));
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
                this.ScoredRefPtsByObs.Add(ctx.Obs.Name, scoredRefPtsByObs[ctx.Obs.Name].ToList());
            }
        }

        public override List<Backproject.Context> FilterAndSortContexts(Vector3 forPoint,
                                                                        List<Backproject.Context> contexts,
                                                                        Dictionary<string, double> scoresByObs = null)
        {
            //indexed by score, sorted high to low (worst first)
            //x=0, y=1 => sign(y - x) = sign(1 - 0) = 1 => x is greater than y
            var bestContexts =
                new SortedDictionary<double, Backproject.Context>(Comparer<double>.Create((x, y) => Math.Sign(y - x)));

            double metersPerSample = 1 / samplesPerMeter;
            double maxDistanceToRefPtSq = 4 * metersPerSample * metersPerSample;

            foreach (var ctx in contexts)
            {
                if (ctx.FrustumHull.Contains(forPoint) && ScoredRefPtsByObs.ContainsKey(ctx.Obs.Name))
                {
                    double bestScoreForObs = double.MaxValue;
                    foreach (var pt in ScoredRefPtsByObs[ctx.Obs.Name])
                    {
                        //heuristic: makes a quality metric from the min pixel spread on the terrain
                        //and the squared distance to ther reference pt
                        //offset by 1 so that if distance ~= 0 scores will still be compared
                        double distanceToRefPtSq = Vector3.DistanceSquared(pt.Point, forPoint);
                        if (distanceToRefPtSq <= maxDistanceToRefPtSq)
                        {
                            bestScoreForObs = Math.Min(bestScoreForObs, (1 + distanceToRefPtSq) * pt.Score);
                        }
                    }
                    if (bestScoreForObs != double.MaxValue)
                    {
                        if (bestContexts.Count == 0 || MaxContexts <= 0 || bestContexts.Count < MaxContexts)
                        {
                            bestContexts[bestScoreForObs] = ctx;
                        }
                        else
                        {
                            double worstScore = bestContexts.First().Key;
                            if (bestScoreForObs < worstScore)
                            {
                                bestContexts.Remove(worstScore);
                                bestContexts[bestScoreForObs] = ctx;
                            }
                        }
                    }
                }
            }

            var sortedContexts = bestContexts.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToList();

            //optionally return scores
            if (scoresByObs != null)
            {
                scoresByObs.Clear();

                foreach (var pair in bestContexts)
                {
                    scoresByObs[pair.Value.Obs.Name] = pair.Key;
                }
            }

            return sortedContexts;
        }
    }
}
