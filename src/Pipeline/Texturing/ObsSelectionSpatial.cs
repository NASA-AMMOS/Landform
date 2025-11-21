using System;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using RTree;
using JPLOPS.Util;
using JPLOPS.MathExtensions;
using JPLOPS.RayTrace;
using JPLOPS.Geometry;

namespace JPLOPS.Pipeline.Texturing
{
    public enum SpatialSelectionMode
    {
        CombinedNeighbors,
        CombinedFilteredNeighbors,
        CombinedWeightedNeighbors,
        CombinedFilteredWeightedNeighbors,
        NearestNeighbor,
        FattestNeighbor,
        BestNeighbor
    };

    // a strategy that samples the mesh at a fixed distribution on the surface
    // exhaustive results are calculated for each sampling point. when final sortings are 
    // performed they use nearby precomputed results according to SelectionMode
    // the goal is higher fidelity than greedy and tunable noisiness that is better than exhaustive
    public class ObsSelectionSpatial : ObsSelectionStrategy
    {
        public override ObsSelectionStrategyName Name { get { return ObsSelectionStrategyName.Spatial; } }

        public SpatialSelectionMode SelectionMode = SpatialSelectionMode.CombinedFilteredWeightedNeighbors;

        private double sampleSpacing, orbitalSampleSpacing;

        private BoundingBox? surfaceBounds;

        private List<Vector3> samples;

        private RTree<int> rTree;

        //sorted contexts per ref point
        private ConcurrentDictionary<int, List<ScoredContext>> scoredContexts =
            new ConcurrentDictionary<int, List<ScoredContext>>();

        public override void Initialize(Mesh mesh, MeshOperator meshOp, SceneCaster meshCaster,
                                        SceneCaster occlusionScene, List<Backproject.Context> contexts)
        {
            if (Logger != null)
            {
                Logger.LogInfo("ObsSelectionSpatial quality: {0}", Quality);
            }

            double orbitalSamplesPerSquareMeter = 0;
            if (OrbitalMetersPerPixel > 0)
            {
                //orbitalSamplesPerSquareMeter = 1 / (OrbitalMetersPerPixel * OrbitalMetersPerPixel);
                orbitalSamplesPerSquareMeter =
                    Quality * TexturingDefaults.OBS_SEL_ORBITAL_QUALITY_TO_SAMPLES_PER_SQUARE_METER;
                orbitalSampleSpacing = SurfacePointSampler.DensityToSampleSpacing(orbitalSamplesPerSquareMeter);
            }

            double samplesPerSquareMeter = Quality * TexturingDefaults.OBS_SEL_QUALITY_TO_SAMPLES_PER_SQUARE_METER;
            if (OrbitalMetersPerPixel > 0 && contexts.Count == 0) //only orbital
            {
                samplesPerSquareMeter = Quality * TexturingDefaults.OBS_SEL_ORBITAL_QUALITY_TO_SAMPLES_PER_SQUARE_METER;
            }

            sampleSpacing = SurfacePointSampler.DensityToSampleSpacing(samplesPerSquareMeter);

            Vector3 meshCtr = meshOp.Bounds.Center();

            if (SurfaceExtent > 0)
            {
                double surfaceBoundsExtent = TexturingDefaults.EXTEND_SURFACE_EXTENT * SurfaceExtent;
                var sb = BoundingBoxExtensions.CreateFromPoint(meshCtr, surfaceBoundsExtent);
                sb.Min.Z = meshOp.Bounds.Min.Z;
                sb.Max.Z = meshOp.Bounds.Max.Z;
                surfaceBounds = sb;
            }

            if (orbitalSamplesPerSquareMeter > 0 && surfaceBounds.HasValue &&
                meshOp.Bounds.Contains(surfaceBounds.Value) == ContainmentType.Contains)
            {
                if (Logger != null)
                {
                    Logger.LogInfo("ObsSelectionSpatial orbital meters per pixel: {0:F3}, " +
                                   "orbital samples per square meter: {1}, orbital sample spacing: {2:F3}, " +
                                   "surface samples per square meter: {3}, surface sample spacing: {3:F3}",
                                   OrbitalMetersPerPixel, orbitalSamplesPerSquareMeter, orbitalSampleSpacing,
                                   samplesPerSquareMeter, sampleSpacing);
                    Logger.LogInfo("ObsSelectionSpatial: mesh bounds: {0}, surface bounds: {1}",
                                   meshOp.Bounds.Fmt(), surfaceBounds.Value.Fmt());
                    Logger.LogInfo("ObsSelectionSpatial: collecting surface samples");
                }

                samples = new SurfacePointSampler()
                    .Sample(meshOp.Clipped(surfaceBounds.Value), samplesPerSquareMeter, positionsOnly: true)
                    .Select(vertex => vertex.Position)
                    .ToList();

                ConsoleHelper.GC(); //this is a memory pinch point

                if (Logger != null)
                {
                    Logger.LogInfo("ObsSelectionSpatial: collecting orbital samples");
                }
                var orbitalSamples = new SurfacePointSampler()
                    .Sample(mesh.Cutted(surfaceBounds.Value), orbitalSamplesPerSquareMeter, positionsOnly: true)
                    .Select(vertex => vertex.Position)
                    .ToList();

                if (Logger != null)
                {
                    Logger.LogInfo("ObsSelectionSpatial surface samples: {0}, orbital samples: {1}",
                                   Fmt.KMG(samples.Count), Fmt.KMG(orbitalSamples.Count));
                }

                ConsoleHelper.GC(); //this is a memory pinch point

                samples.AddRange(orbitalSamples);
            }
            else
            {
                surfaceBounds = null;

                if (Logger != null)
                {
                    Logger.LogInfo("ObsSelectionSpatial samples per square meter: {0}, sample spacing: {1:F3}",
                                   samplesPerSquareMeter, sampleSpacing);
                    Logger.LogInfo("ObsSelectionSpatial: collecting surface samples");
                }

                samples = new SurfacePointSampler()
                    .Sample(mesh, samplesPerSquareMeter, positionsOnly: true)
                    .Select(vertex => vertex.Position)
                    .ToList();

                if (Logger != null)
                {
                    Logger.LogInfo("ObsSelectionSpatial samples: {0}", Fmt.KMG(samples.Count));
                }
            }

            ConsoleHelper.GC(); //this is a memory pinch point

            //we had a bug here
            //we were getting way more points than thought we asked for
            //a typical setting was Quality=0.05 meaning 5 samples per square meter
            //in practice we were getting about 6 times that
            //though that seems a more reasonable number to use for our purposes here than 5
            //so this may be a case of Quality=0.05 was already tuned to compensate for this bug
            //
            //the problem is, our estimation of sampleSpacing above assumed that SurfacePointSampler would
            //do the right thing and return approximately the requested samplesPerSquareMeter
            //we need to compensate estimating sampleSpacing
            //otherwise below in the FilterAndSortContexts() hotpath
            //we will match on way too many reference points for each mesh point
            //this can have an effect like *doubling* the total time spent in backproject
            //with no noticeable change in result
            //
            //that bug should be fixed now
            //(well it does require computing the mesh surface area)
            //double area = mesh.SurfaceArea();
            //double actualDensity = samples.Count / area;
            //double actualSampleSpacing = 1 / Math.Sqrt(2 * actualDensity);
            //Console.WriteLine("generated {0} samples for {1}m^2 mesh (density {2} samples / m^2), requested {3}, " +
            //                  "correcting sample spacing from {4} to {5}", samples.Count, area, actualDensity,
            //                  samplesPerSquareMeter, sampleSpacing, actualSampleSpacing);
            //sampleSpacing = actualSampleSpacing;

            if (samples.Count == 0) //handle case of very small mesh
            {
                samples.Add(meshCtr);
            }

            //add center point of each observation to make sure small fov images are considered
            foreach (var ctx in contexts)
            {
                var ctrPixel = new Vector2(0.5 * ctx.Obs.Width, 0.5 * ctx.Obs.Height);
                Vector3? res = Backproject.RaycastMesh(ctx.CameraModel, ctx.ObsToMesh, ctrPixel, occlusionScene,
                                                       meshCaster, meshOp.Bounds, RaycastTolerance);
                if (res.HasValue)
                {
                    samples.Add(res.Value);
                }
            }

            if (!string.IsNullOrEmpty(DebugOutputPath))
            {
                mesh.Save(PathHelper.EnsureDir(DebugOutputPath, "sceneMesh.ply"));
                var sampledMesh = new Mesh();
                sampledMesh.Vertices = samples.Select(pt => new Vertex(pt)).ToList();
                sampledMesh.Save(PathHelper.EnsureDir(DebugOutputPath, "spatialSamplePts.ply"));
            }

            //build RTree
            rTree = new RTree<int>();
            for (int i = 0; i < samples.Count; i++)
            {
                rTree.Add(samples[i].ToRectangle(), i);
            }

            //exhaustively sort for each sample point
            var refSelect = new ObsSelectionExhaustive();
            refSelect.Quality = Quality; //ISSUE 1091: should have an independent control for exhaustive quality
            refSelect.MaxContexts = MaxContexts;
            refSelect.EquivalentScoresAbs = EquivalentScoresAbs;
            refSelect.EquivalentScoresRel = EquivalentScoresRel;
            refSelect.PreferColor = PreferColor;
            refSelect.OrbitalMetersPerPixel = OrbitalMetersPerPixel;
            refSelect.DebugOutputPath = DebugOutputPath;
            refSelect.RaycastTolerance = RaycastTolerance;

            refSelect.Initialize(mesh, meshOp, meshCaster, occlusionScene, contexts);

            //collect a sorted list of contexts (best to worst) for each sample point
            //only keeps contexts that have better effective resultion for the sample point than orbital (if any)
            CoreLimitedParallel.For(0, samples.Count, i =>
            {
                scoredContexts[i] = refSelect.FilterAndSortContexts(samples[i], contexts, meshCaster);
                if (!string.IsNullOrEmpty(DebugOutputPath) && scoredContexts[i].Count > 0)
                {
                    string filename = $"RefScoresForPoint_{samples[i].X}_{samples[i].Y}_{samples[i].Z}.txt";
                    using (StreamWriter sw = new StreamWriter(PathHelper.EnsureDir(DebugOutputPath, filename)))
                    {
                        sw.WriteLine(string.Format("{0}: {1}", "Observation Name", "Score (lower is better)"));
                        foreach (var ctx in scoredContexts[i])
                        {
                            sw.WriteLine(string.Format("{0}: {1}", ctx.Context.Obs.Name, ctx.Score));
                        }
                    }
                }
            });
        }

        //this gets called in a smoking hot inner loop in Backproject
        //small stuff here can get very expensive
        public override List<ScoredContext> FilterAndSortContexts(Vector3 meshPoint, List<Backproject.Context> contexts,
                                                                  SceneCaster meshCaster = null)
        {
            if (contexts == null || contexts.Count == 0)
            {
                return null;
            }

            if (!meshPoint.IsFinite())
            {
                return null; //backproject options sampleTransform (used e.g. by BuildSkySphere) can kill points
            }

            double spacing = sampleSpacing;
            if (surfaceBounds.HasValue && !surfaceBounds.Value.ContainsPoint(meshPoint))
            {
                spacing = orbitalSampleSpacing;
            }

            double searchRadius = TexturingDefaults.OBS_SEL_SEARCH_RADIUS_SAMPLES * spacing;
            var searchBounds = BoundingBoxExtensions.CreateFromPoint(meshPoint, 2 * searchRadius).ToRectangle();
            var neighborIndices = rTree.Intersects(searchBounds);

            while (neighborIndices.Count == 0 && searchRadius < 10 * spacing)
            {
                //shouldn't get here often, but there is randomness in this world
                searchRadius += 0.5 * spacing;
                searchBounds = BoundingBoxExtensions.CreateFromPoint(meshPoint, 2 * searchRadius).ToRectangle();
                neighborIndices = rTree.Intersects(searchBounds);
            }

            switch (SelectionMode)
            {
                case SpatialSelectionMode.CombinedNeighbors:
                case SpatialSelectionMode.CombinedFilteredNeighbors:
                {
                    bool filter = SelectionMode == SpatialSelectionMode.CombinedFilteredNeighbors;
                    var bestContexts = new BestContexts(this);
                    foreach (var sampleIndex in neighborIndices)
                    {
                        //FrustumHull.Contains() seems to be about 4x as expensive as Backproject.InFrame() here
                        //also InFrame() uses the (possibly nonlinear) camera model and gives a better answer
                        //in the nonlin case the hull can be poorly fitting
                        foreach (var ctx in scoredContexts[sampleIndex]
                                 //.Where(c => !filter || c.Context.FrustumHull.Contains(meshPoint)))
                                 .Where(c => !filter || Backproject.InFrame(meshPoint, c.Context)))
                        {
                            if (Backproject.InFrame(meshPoint, ctx.Context))
                            {
                                bestContexts.Add(ctx);
                            }
                        }
                    }
                    return bestContexts.GetSortedContexts();
                }

                case SpatialSelectionMode.CombinedWeightedNeighbors:
                case SpatialSelectionMode.CombinedFilteredWeightedNeighbors:
                {
                    //furthest neighbor's scores will be doubled (lower scores are better)
                    //nearest neighbor's scores will be unmodified
                    bool filter = SelectionMode == SpatialSelectionMode.CombinedFilteredWeightedNeighbors;
                    var bestContexts = new BestContexts(this);
                    double nearest = double.PositiveInfinity, furthest = double.NegativeInfinity;
                    foreach (var sampleIndex in neighborIndices)
                    {
                        double dist = Vector3.Distance(meshPoint, samples[sampleIndex]);
                        nearest = Math.Min(nearest, dist);
                        furthest = Math.Max(furthest, dist);
                    }
                    foreach (var sampleIndex in neighborIndices)
                    {
                        double dist = Vector3.Distance(meshPoint, samples[sampleIndex]);
                        double factor = furthest - nearest;
                        factor = factor > 1e-6 ? (1 / factor) : 1;
                        double weight = 1 + MathE.Clamp01(factor * (dist - nearest));
                        //weight = weight * weight; //could make it quadratic drop off
                        foreach (var ctx in scoredContexts[sampleIndex]
                                 .Where(c => !filter || Backproject.InFrame(meshPoint, c.Context)))
                        {
                            bestContexts.Add(ctx.Context, ctx.Score * weight);
                        }
                    }
                    return bestContexts.GetSortedContexts();
                }

                case SpatialSelectionMode.NearestNeighbor:
                {
                    double nearestDistSq = double.PositiveInfinity;
                    List<ScoredContext> bestContexts = null;
                    foreach (var sampleIndex in neighborIndices)
                    {
                        double distSq = Vector3.DistanceSquared(meshPoint, samples[sampleIndex]);
                        if (distSq < nearestDistSq)
                        {
                            nearestDistSq = distSq;
                            bestContexts = scoredContexts[sampleIndex];
                        }
                    }
                    return bestContexts;
                }

                case SpatialSelectionMode.FattestNeighbor:
                {
                    List<ScoredContext> bestContexts = null;
                    foreach (var sampleIndex in neighborIndices)
                    {
                        if (bestContexts == null || scoredContexts[sampleIndex].Count > bestContexts.Count)
                        {
                            bestContexts = scoredContexts[sampleIndex];
                        }
                    }
                    return bestContexts;
                }

                case SpatialSelectionMode.BestNeighbor:
                {
                    double bestScore = double.PositiveInfinity;
                    List<ScoredContext> bestContexts = null;
                    foreach (var sampleIndex in neighborIndices)
                    {
                        var neighborContexts = scoredContexts[sampleIndex];
                        if (neighborContexts.Count > 0)
                        {
                            var bestNeighborScore = neighborContexts[0].Score;
                            if (bestNeighborScore < bestScore)
                            {
                                bestContexts = neighborContexts;
                                bestScore = bestNeighborScore;
                            }
                        }
                    }
                    return bestContexts;
                }

                default: throw new Exception("unknown selection mode: " + SelectionMode);
            }
        }
    }
}
