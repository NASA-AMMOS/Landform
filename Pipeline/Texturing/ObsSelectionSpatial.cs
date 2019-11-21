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
    class ObsSelectionSpatial : ObsSelectionStrategy
    {
        protected struct ScoredPoint
        {
            public PixelPoint Pt;
            public double Score;

            public ScoredPoint(PixelPoint pt, double score)
            {
                Pt = pt;
                Score = score;
            }
        }
        List<Backproject.Context> Contexts;
        Dictionary<string, List<ScoredPoint>> ScoredRefPtsByObs;
        Dictionary<string, Backproject.Context> ObsToContext;

        public override void Initialize(Mesh mesh, ConvexHull meshHull, MeshOperator meshOp, SceneCaster occlusionScene, 
                               List<Backproject.Context> contexts, int outputTextureResolution,double quality, bool writeDebug, string localOutputPath)
        {
            //select points on the surface of the mesh at requested density to use for sampling backproject
            double samplesPerMeter = quality * 100.0;

            //adjust sampling ratio to be higher when tiles fall below a meter, fine tiles indicate
            // high resolution data available and are worth the extra sampling
            //potentially mission specific: assumes z vertical
            //double minBounds = Math.Min(meshOp.Bounds.Extent().X, meshOp.Bounds.Extent().Y);
            //if(minBounds < 1.0)
            //{
            //    samplesPerMeter *= 1 / minBounds;
            //}

            Mesh sampledMesh = new SurfacePointSampler().GenerateSampledMesh(mesh, samplesPerMeter);

            //guarantee at least a single point on the mesh
            while(sampledMesh.Vertices.Count == 0)
            {
                samplesPerMeter += 1;
                sampledMesh = new SurfacePointSampler().GenerateSampledMesh(mesh, samplesPerMeter);
            }

            if (writeDebug)
            {
                sampledMesh.Save(Path.Combine(localOutputPath, "spatialSamplePts.ply"));
                meshHull.Mesh.Save(Path.Combine(localOutputPath, "meshHull.ply"));
            }

            //heuristic: add points on the border (to make sure the edge get similar choices to neighbors
            //potentially mission specific: assumes z down, < 1km height
            const double safeHeight = -1000;
            Vector3 dirDown = new Vector3(0, 0, 1);
            //Vector3[] rayPts = new Vector3[]
            //{
            //    new Vector3(meshOp.Bounds.Min.X, meshOp.Bounds.Min.Y, safeHeight),
            //    new Vector3(meshOp.Bounds.Min.X, meshOp.Bounds.Max.Y, safeHeight),
            //    new Vector3(meshOp.Bounds.Max.X, meshOp.Bounds.Min.Y, safeHeight),
            //    new Vector3(meshOp.Bounds.Max.X, meshOp.Bounds.Max.Y, safeHeight),
            //    new Vector3((meshOp.Bounds.Min.X + meshOp.Bounds.Max.X)*0.5, meshOp.Bounds.Min.Y, safeHeight),
            //    new Vector3(meshOp.Bounds.Min.X, (meshOp.Bounds.Min.Y + meshOp.Bounds.Max.Y)*0.5, safeHeight),
            //    new Vector3((meshOp.Bounds.Min.X + meshOp.Bounds.Max.X)*0.5, meshOp.Bounds.Max.Y, safeHeight),
            //    new Vector3(meshOp.Bounds.Max.X, (meshOp.Bounds.Min.Y + meshOp.Bounds.Max.Y)*0.5, safeHeight)
            //};

            //heuristic: add points on the mesh hull (bounds is axis aligned off the mesh)
            //potentially mission specific: assumes z vertical, < 1km height
            Mesh seedPts = new Mesh(meshHull.Mesh);
            seedPts.Clean();
            foreach (var pt in seedPts.Vertices)
            {
                var rayPt = new Vector3(pt.Position.X, pt.Position.Y, safeHeight);
                var rayResult = occlusionScene.Raycast(new Ray(rayPt, dirDown));
                if (rayResult != null)
                {
                    sampledMesh.Vertices.Add(new Vertex(rayResult.Position));
                }
            }

            //heuristic: add center point of each observation (to make sure small fov images are not missed)
            foreach (var ctx in contexts)
            {
                CameraModel cam = (CameraModel)JsonHelper.FromJson(ctx.Obs.CameraModel);
                Vector2 pixel = new Vector2(ctx.Obs.Width / 2.0, ctx.Obs.Height / 2.0);
                Vector3? res = Backproject.RaycastMesh(cam, ctx.ObsToMesh, pixel, occlusionScene);
                if(res.HasValue)
                {
                    sampledMesh.Vertices.Add(new Vertex(res.Value));
                }
            }

            if (writeDebug)
            {
                sampledMesh.Save(Path.Combine(localOutputPath, "extendedSpatialSamplePts.ply"));
            }

            //filter these points to remove points that happen to map back to the same output texture pixel
            // large areas on the mesh can map to small areas in the texture atlas
            var grouped = sampledMesh.Vertices.GroupBy(
                v => new Pixel((int)Image.UVToPixel(v.UV, outputTextureResolution, outputTextureResolution).Y, (int)Image.UVToPixel(v.UV, outputTextureResolution, outputTextureResolution).X),
                v => v);
            List<Vertex> filteredSampledVerts = grouped.Select(g => g.First()).ToList();

            // convert verts to sample points and filter for any that are located off the destination texture
            List<PixelPoint> referencePoints = filteredSampledVerts.Select(v => new PixelPoint() { Pixel = Image.UVToPixel(v.UV, outputTextureResolution, outputTextureResolution), Point = v.Position })
            .Where(pp => pp.Pixel.X >= 0 && pp.Pixel.X < outputTextureResolution && pp.Pixel.Y >= 0 && pp.Pixel.Y < outputTextureResolution).ToList();

            // initialize datastructures 
            Contexts = new List<Backproject.Context>(contexts);
            ScoredRefPtsByObs = new Dictionary<string, List<ScoredPoint>>();
            ObsToContext = new Dictionary<string, Backproject.Context>();
            foreach(var ctx in Contexts)
            {
                ScoredRefPtsByObs.Add(ctx.Obs.Name, new List<ScoredPoint>());
                ObsToContext.Add(ctx.Obs.Name, ctx);
            }

            // build db of scores and locations by observation
            ObsSelectionExhaustive refSelect = new ObsSelectionExhaustive();
            refSelect.Initialize(mesh, meshHull, meshOp, occlusionScene, contexts, outputTextureResolution, quality, writeDebug, localOutputPath);
            foreach(var pt in referencePoints)
            {
                refSelect.FilterAndSortContexts(pt, out ConcurrentDictionary<string, double> ptScoresByObs);
                foreach(var score in ptScoresByObs)
                {
                    ScoredRefPtsByObs[score.Key].Add(new ScoredPoint(pt, score.Value));
                    //TODO: save?
                }
            };
        }

        public override List<Backproject.Context> FilterAndSortContexts(PixelPoint forPixel, out ConcurrentDictionary<string, double> scoresByObs)
        {
            //find distances, save maximum for weighting
            Dictionary<string, List<Tuple<double, ScoredPoint>>> groupedByObs = new Dictionary<string, List<Tuple<double, ScoredPoint>>>();
            double maxDistance = double.MinValue;
            foreach (var obsName in ScoredRefPtsByObs.Keys)
            {
                //early out if context has no chance for pt
                if (!ObsToContext[obsName].FrustumHull.Contains(forPixel.Point))
                    continue;

                groupedByObs[obsName] = new List<Tuple<double, ScoredPoint>>();

                foreach (var refScoredPt in ScoredRefPtsByObs[obsName])
                {
                    double dist = Vector3.Distance(refScoredPt.Pt.Point, forPixel.Point);
                    if (dist > maxDistance)
                    {
                        maxDistance = dist;
                    }

                    groupedByObs[obsName].Add(new Tuple<double, ScoredPoint>(dist, refScoredPt));
                }
            }

            //find best score for each observation
            Dictionary<string, double> bestEntries = new Dictionary<string, double>();
            foreach (var obs in groupedByObs.Keys)
            {
                double minWeightedScore = double.MaxValue;
                //bestEntries[obs] = double.MaxValue; //ensures an en
                foreach (var pt in groupedByObs[obs])
                {
                    //heuristic: assigns equal value to distance from sample point and the min pixel spread on the terrain
                    //TODO: better, more physical weighting?
                    double weightedScore = pt.Item1 / maxDistance * pt.Item2.Score;
                    if(weightedScore < minWeightedScore)
                    {
                        bestEntries[obs] = weightedScore;
                    }
                }
            }

            //sort contexts by their scores (and return them)
            //TODO: undo weighting by multiplying by max dist?
            List<Backproject.Context> sortedContexts = Contexts.Where(c => bestEntries.ContainsKey(c.Obs.Name)).ToList();
            sortedContexts.Sort((ctx0, ctx1) => bestEntries[ctx0.Obs.Name].CompareTo(bestEntries[ctx1.Obs.Name]));
            scoresByObs = new ConcurrentDictionary<string, double>(bestEntries);
            return sortedContexts;
        }

    }
}
