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
        Dictionary<string, ConcurrentBag<ObsSelectionStrategy.ScoredPoint>> ScoredRefPtsByObs;

        public override void Initialize(Mesh mesh, MeshOperator meshOp, SceneCaster occlusionScene, 
                               List<Backproject.Context> allContexts, int outputTextureResolution,double quality,
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
            ScoredRefPtsByObs = new Dictionary<string, ConcurrentBag<ObsSelectionStrategy.ScoredPoint>>();
            foreach (var ctx in allContexts)
            {
                ScoredRefPtsByObs.Add(ctx.Obs.Name, new ConcurrentBag<ObsSelectionStrategy.ScoredPoint>());
                ObsToContext.Add(ctx.Obs.Name, ctx);
            }

            //collect a sorted list of contexts (best to worst) for each sample point
            CoreLimitedParallel.ForEach(sampledMesh.Vertices.Select(v => v.Position), pt =>
            {
                string ptDebugPath = Path.Combine(localOutputPath, "Point_" + pt.X + "_" + pt.Y + "_" + pt.Z);

                //exhaustively sort for each sample point
                ObsSelectionExhaustive refSelect = new ObsSelectionExhaustive();
                refSelect.Initialize(mesh, meshOp, occlusionScene, allContexts, outputTextureResolution, quality, writeDebug, ptDebugPath);
                var sortedContexts = refSelect.FilterAndSortContexts(pt, allContexts, out ConcurrentDictionary<string, double> ptScoresByObs);

                foreach (var pair in ptScoresByObs)
                {
                    ScoredRefPtsByObs[pair.Key].Add(new ObsSelectionStrategy.ScoredPoint(pt, pair.Value));
                }

                if (writeDebug)
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
        }

        public override List<Backproject.Context> FilterAndSortContexts(Vector3 forPoint, List<Backproject.Context> prunedContexts, out ConcurrentDictionary<string, double> scoresByObs)
        {
            //find distances, save maximum for weighting
            Dictionary<string, List<Tuple<double, ScoredPoint>>> refPtDistancesByObs = new Dictionary<string, List<Tuple<double, ScoredPoint>>>();
            double maxDistance = double.MinValue;
            foreach (var ctx in prunedContexts)
            {
                if (!ObsToContext.ContainsKey(ctx.Obs.Name))
                    throw new Exception("Unexpected context as compared to init: "+ ctx.Obs.Name);

                //early out if context has no chance for pt
                if (!ctx.FrustumHull.Contains(forPoint))
                    continue;

                //collect distance between reference pt and current point
                refPtDistancesByObs[ctx.Obs.Name] = new List<Tuple<double, ScoredPoint>>();
                foreach (var refScoredPt in ScoredRefPtsByObs[ctx.Obs.Name])
                {
                    double dist = Vector3.Distance(refScoredPt.Point, forPoint);
                    if (dist > maxDistance)
                    {
                        maxDistance = dist;
                    }

                    refPtDistancesByObs[ctx.Obs.Name].Add(new Tuple<double, ScoredPoint>(dist, refScoredPt));
                }
            }

            //find best weighted score for each observation
            Dictionary<string, double> bestWeightedScoreByObs = new Dictionary<string, double>();
            foreach (var obs in refPtDistancesByObs.Keys)
            {
                double minWeightedScore = double.MaxValue;
                foreach (var pt in refPtDistancesByObs[obs])
                {
                    //heuristic: assigns equal value to distance from sample point and the min pixel spread on the terrain
                    double weightedScore = pt.Item1 / maxDistance * pt.Item2.Score;
                    if (weightedScore < minWeightedScore)
                    {
                        bestWeightedScoreByObs[obs] = weightedScore;
                    }
                }
            }

            ////sort contexts by their scores (and return them)
            List<Backproject.Context> sortedContexts = prunedContexts.Where(c => bestWeightedScoreByObs.ContainsKey(c.Obs.Name)).ToList();
            sortedContexts.Sort((ctx0, ctx1) => bestWeightedScoreByObs[ctx0.Obs.Name].CompareTo(bestWeightedScoreByObs[ctx1.Obs.Name]));
            scoresByObs = new ConcurrentDictionary<string, double>(bestWeightedScoreByObs);
            return sortedContexts;

        }

    }
}
