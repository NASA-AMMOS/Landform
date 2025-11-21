using System.Linq;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;

namespace JPLOPS.Pipeline
{
    /// <summary>
    /// Splitting criteria to split tiles based on a max mesh area
    /// </summary>
    public class AreaSplitCriteria : TileSplitCriteria
    {
        public readonly double maxArea; //unlimited if non-positive

        public AreaSplitCriteria(double maxArea)
        {
            this.maxArea = maxArea;
        }

        public string ShouldSplit(BoundingBox bounds, params MeshOperator[] meshOps)
        {
            if (maxArea <= 0)
            {
                return null;
            }
            double area = meshOps.Sum(meshOp => meshOp.ClippedMeshArea(bounds));
            return area > maxArea ? $"{area:f3} > {maxArea:f3} m^2" : null;
        }
    }
}
