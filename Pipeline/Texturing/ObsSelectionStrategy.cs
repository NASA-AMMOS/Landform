using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.RayTrace;
using JPLOPS.Geometry;
using JPLOPS.Pipeline.AlignmentServer;

namespace JPLOPS.Pipeline.Texturing
{
    public enum ObsSelectionStrategyName { Exhaustive, Spatial }; 

    public enum PreferColorMode { Never, Always, EquivalentScores }; 

    public abstract class ObsSelectionStrategy
    {
        public ILogger Logger;

        public abstract ObsSelectionStrategyName Name { get; }

        public double Quality = TexturingDefaults.OBS_SEL_QUALITY; //in range [0,1]

        //Testing indicates that nearly all backprojects are satisfied within the first ~32 tried contexts.
        //Run with --verbosebackproject to get a report of the max number of contexts tried.
        //Limiting the total number is valuable because ObsSelectionSpatial combines them in a very hot inner loop.
        public int MaxContexts = TexturingDefaults.OBS_SEL_MAX_CONTEXTS; //unlimited if non-positive

        //If the difference in two scores is less than either (a) EquivalentScoresAbs or
        //(b) EquivalentScoresRel times their average, then consider them "equivalent".
        //This can be disabled by setting both to 0.
        //If two observations are "equivalent" that does not mean we drop one of them.
        //They are both kept, but we may sort them differently.
        //For example, if PreferColor=EquivalentScores and they have different numbers of bands then the one with more
        //bands is preferred, even if it has a lower score.
        public double EquivalentScoresAbs = TexturingDefaults.OBS_SEL_EQUIVALENT_SCORES_ABS; //units: meters per texel
        public double EquivalentScoresRel = TexturingDefaults.OBS_SEL_EQUIVALENT_SCORES_REL;

        //either never prefer color, always prefer it, or prefer it within a score equivalency class
        public PreferColorMode PreferColor = TexturingDefaults.OBS_SEL_PREFER_COLOR;

        //within a score equivalency class, prefer surface over orbital observations
        public bool PreferSurface = TexturingDefaults.OBS_SEL_PREFER_SURFACE;

        //within a score equivalency class, prefer nonlinear over linear observations
        public bool PreferNonlinear = TexturingDefaults.OBS_SEL_PREFER_NONLINEAR;

        //smallest distance in meters for a raycast to be significant
        public double RaycastTolerance = TexturingDefaults.RAYCAST_TOLERANCE;

        public string DebugOutputPath; //null disables debug output

        public double OrbitalMetersPerPixel; //0 disables orbital
        public bool OrbitalIsColor;

        public double SurfaceExtent; //size in meters of central square of mesh with surface geometry, 0 if only orbital

        public static ObsSelectionStrategy Create(ObsSelectionStrategyName name)
        {
            switch (name)
            {
                case ObsSelectionStrategyName.Exhaustive: return new ObsSelectionExhaustive();
                case ObsSelectionStrategyName.Spatial: return new ObsSelectionSpatial();
                default: throw new Exception("Unknown ObsSelectionStrategy: " + name);
            }
        }

        //meshcaster: the raycasting target of the mesh
        //occlusionscene: the raycasting  target for occlusion checking, may be same as meshCaster
        public abstract void Initialize(Mesh mesh, MeshOperator meshOp, SceneCaster meshCaster,
                                        SceneCaster occlusionScene, List<Backproject.Context> contexts);

        public class ScoredContext
        {
            public Backproject.Context Context;
            public double Score;
        }

        //returns observations that can see meshPoint ordered in increasing order of meters on mesh per pixel in obs
        //returns up to MaxContexts filtered and sorted contexts
        //if meshCaster is specified it overrides the one that was passed to Initialize()
        public abstract List<ScoredContext> FilterAndSortContexts(Vector3 meshPoint, List<Backproject.Context> contexts,
                                                                  SceneCaster meshCaster = null);

        protected class BestContexts
        {
            public int Count { get { return scoredCtx.Count; } }

            private ObsSelectionStrategy strategy;
            private Dictionary<Backproject.Context, ScoredContext> scoredCtx =
                new Dictionary<Backproject.Context, ScoredContext>();

            public BestContexts(ObsSelectionStrategy strategy)
            {
                this.strategy = strategy;
            }

            public void Add(Backproject.Context ctx, double score)
            {
                Add(new ScoredContext() { Context = ctx, Score = score });
            }

            public void Add(ScoredContext sc)
            {
                //save the best (lowest) score for each observation
                //this gets called in a flaming hot codepath so perf is critical
                //not worried about bounding memory use here
                //there is one instance of BestContexts per thread
                //and CoreLimitedParallel means we have a bounded number of threads
                if (!scoredCtx.ContainsKey(sc.Context) || (sc.Score < scoredCtx[sc.Context].Score))
                {
                    scoredCtx[sc.Context] = sc;
                }
            }

            public List<ScoredContext> GetSortedContexts()
            {
                //this is a bit tricky, and it gets called in a kind of hot codepath
                //we need to sort the contexts from best (lowest score) to worst
                //we also want to respect MaxContexts, if positive, to bound memory use
                //and this is where we implement score equivalence classes and color, surface, and linear preferences

                //we build equivalence classs of groups of observations from best to worst
                //all observations in group n are better than all observations in groups m > n
                //each group is then internally sorted by secondary criteria

                //we can't just do a single sort by a comparator that implements the equivalency classes
                //because such a comparator would not be transitive

                double equivAbs = Math.Max(strategy.EquivalentScoresAbs, 0);
                double equivRel = Math.Max(strategy.EquivalentScoresRel, 0);

                //check if two scores are in the same equivalence class
                bool equivalentScores(ScoredContext a, ScoredContext b)
                {
                    double diff = Math.Abs(a.Score - b.Score);
                    return diff <= equivAbs || diff <= equivRel * 0.5 * (a.Score + b.Score);
                }

                var preferColor = strategy.PreferColor;
                bool preferSurface = strategy.PreferSurface || strategy.OrbitalMetersPerPixel <= 0;
                bool preferNonLinear = strategy.PreferNonlinear;

                //negative when a < b, 0 when a == b, positive when a > b
                IComparer<ObsSelectionStrategy.ScoredContext> makeComparer(bool forGroup)
                {
                    return Comparer<ObsSelectionStrategy.ScoredContext>.Create((a, b) =>
                    {
                        Observation oa = a.Context.Obs, ob = b.Context.Obs;

                        //negative if a.Bands > b.Bands (more bands is better)
                        int bandDiff = preferColor != PreferColorMode.Never ? (ob.Bands - oa.Bands) : 0;

                        //negative if a surface, b orbital (surface is better)
                        int surfaceDiff = preferSurface ? ((oa.IsOrbital ? 1 : 0) - (ob.IsOrbital ? 1 : 0)) : 0;

                        //negative if a nonlinear, b linear (nonlinear is better)
                        int linearDiff = preferNonLinear ? ((oa.IsLinear ? 1 : 0) - (ob.IsLinear ? 1 : 0)) : 0;

                        //negative if a.Score < b.Score (lower scores are better)
                        int scoreDiff = Math.Sign(a.Score - b.Score); 

                        if (forGroup)
                        {                                            //within a group:
                            return (bandDiff != 0 ? bandDiff :       //sort first by color / grayscale (maybe)
                                    surfaceDiff != 0 ? surfaceDiff : //then by surface / orbital (maybe)
                                    linearDiff != 0 ? linearDiff :   //then by linear / nonlinear (maybe)
                                    scoreDiff != 0 ? scoreDiff :     //then by score low to high
                                    oa.Name.CompareTo(ob.Name));     //last by name
                        }
                        else
                        {
                            if (preferColor != PreferColorMode.Always)
                            {
                                bandDiff = 0;
                            }                                        //globally:
                            return (bandDiff != 0 ? bandDiff :       //sort first by color / grayscale (maybe)
                                    scoreDiff != 0 ? scoreDiff :     //then by score low to high
                                    surfaceDiff != 0 ? surfaceDiff : //then by surface / orbital (maybe)
                                    linearDiff != 0 ? linearDiff :   //then by nonlinear / linear (maybe)
                                    oa.Name.CompareTo(ob.Name));     //last by name
                        }
                    });
                }

                var globalComparer = makeComparer(forGroup: false);
                var groupComparer = makeComparer(forGroup: true);

                var best = new List<ScoredContext>();
                int groupStart = 0, groupSize = 0;
                foreach (var ctx in scoredCtx.Values.OrderBy(sc => sc, globalComparer))
                {
                    if (best.Count == 0 || equivalentScores(best[groupStart], ctx))
                    {
                        best.Add(ctx);
                        groupSize++;
                    }
                    else //not the very first one, and not part of the last equivalency class group: start a new group
                    {
                        if (groupSize > 0)
                        {
                            best.Sort(groupStart, groupSize, groupComparer); //sort the previous group
                        }
                        groupStart = best.Count;
                        groupSize = 0;
                        best.Add(ctx);
                    }
                    if (strategy.MaxContexts > 0 && best.Count >= strategy.MaxContexts)
                    {
                        if (groupSize > 0)
                        {
                            best.Sort(groupStart, groupSize, groupComparer); //sort the last group
                        }
                        break;
                    }
                }
                return best;
            }
        }
    }
}
