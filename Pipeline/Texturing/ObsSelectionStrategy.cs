using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;
using OPS.RayTrace;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline.Texturing
{
    public enum ObsSelectionStrategyName
    {
        Exhaustive,
        Spatial
    };

    public abstract class ObsSelectionStrategy
    {
        public abstract ObsSelectionStrategyName Name { get; }

        public double OrbitalMetersPerPixel; //default 0 disables orbital

        public string DebugOutputPath; //null disables debug output

        public struct ScoredPoint
        {
            public Vector3 Point;
            public double Score;

            public ScoredPoint(Vector3 point, double score)
            {
                Point = point;
                Score = score;
            }
        }

        public bool PreferLinearToNonlinear()
        {
            // all current observation selection strategies are based on source pixel density on the terrain
            // any resampling (such as during a linearization) can hide the original pixel density
            return false;
        }

        public static ObsSelectionStrategy Create(ObsSelectionStrategyName name)
        {
            switch (name)
            {
                case Texturing.ObsSelectionStrategyName.Exhaustive: return new Texturing.ObsSelectionExhaustive();
                case Texturing.ObsSelectionStrategyName.Spatial: return new Texturing.ObsSelectionSpatial();
                default: throw new Exception("Unknown ObsSelectionStrategy: " + name);
            }
        }

        public abstract void Initialize(Mesh mesh, MeshOperator meshOp, SceneCaster meshCaster, SceneCaster occlusionScene,
                                        List<Backproject.Context> contexts, int outputTextureResolution,
                                        double raycastTolerance, double quality = 1);

        //sorts observations from best to worst
        //returns filtered and sorted contexts
        //optionally returns scores for each observation
        public abstract List<Backproject.Context> FilterAndSortContexts(Vector3 forPoint, SceneCaster meshCaster,
                                                                        List<Backproject.Context> contexts,
                                                                        Dictionary<string, double> scoresByObs = null);
    }
}
