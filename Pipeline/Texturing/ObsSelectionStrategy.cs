using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;
using OPS.RayTrace;

namespace OPS.Pipeline.Texturing
{
    public enum ObsSelectionStrategyName
    {
        Exhaustive,
        Greedy,
        Spatial
    };

    
    public abstract class ObsSelectionStrategy
    {
        public static ObsSelectionStrategy Create(ObsSelectionStrategyName name)
        {
            switch (name)
            {
                case Texturing.ObsSelectionStrategyName.Exhaustive:
                    return new Texturing.ObsSelectionExhaustive();
                case Texturing.ObsSelectionStrategyName.Greedy:
                    return new Texturing.ObsSelectionGreedy();
                case Texturing.ObsSelectionStrategyName.Spatial:
                    return new Texturing.ObsSelectionSpatial();
                default:
                    throw new Exception("Unknown ObsSelectionStrategy: " + name);
            }
        }

        public abstract void Initialize(Mesh mesh, ConvexHull meshHull, MeshOperator meshOp, SceneCaster occlusionScene,
                               List<Backproject.Context> contexts, int outputTextureResolution, double quality);

        //sorts observations from best to worst
        public abstract List<Backproject.Context> SortContexts(PixelPoint forPixel, out ConcurrentDictionary<string, double> scoresByObs);
    }
}
