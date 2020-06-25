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
    public enum ObsSelectionStrategyName { Exhaustive, Spatial }; 

    public abstract class ObsSelectionStrategy
    {
        public abstract ObsSelectionStrategyName Name { get; }

        public double OrbitalMetersPerPixel; //default 0 disables orbital

        public string DebugOutputPath; //null disables debug output

        public int MaxContexts = 10; //unlimited if non-positive

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
                case ObsSelectionStrategyName.Exhaustive: return new ObsSelectionExhaustive();
                case ObsSelectionStrategyName.Spatial: return new ObsSelectionSpatial();
                default: throw new Exception("Unknown ObsSelectionStrategy: " + name);
            }
        }

        //meshcaster: the raycasting target of the mesh
        //occlusionscene: the raycasting  target for occlusion checking, may be same as meshCaster
        public abstract void Initialize(Mesh mesh, MeshOperator meshOp, SceneCaster meshCaster,
                                        SceneCaster occlusionScene, double raycastTolerance,
                                        List<Backproject.Context> contexts, int outputTextureResolution,
                                        double quality = 1, bool preferColor = true);

        //sorts observations from best to worst
        //returns up to MaxContexts filtered and sorted contexts
        //optionally returns scores for each observation
        //if meshCaster is specified it overrides the one that was passed to Initialize
        public abstract List<Backproject.Context> FilterAndSortContexts(Vector3 forPoint,
                                                                        List<Backproject.Context> contexts,
                                                                        SceneCaster meshCaster = null,
                                                                        Dictionary<string, double> scoresByObs = null);
    }
}
