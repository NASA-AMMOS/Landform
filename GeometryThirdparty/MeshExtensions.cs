using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    public static class MeshExtensions
    {

        public static Mesh ResampleDecimation(this Mesh m, int targetNumberOfFaces = 2000, BoundingBox? clippingBounds = null, Vector3? cornerDirection = null)
        {
            // First make a copy
            m = new Mesh(m);
            m.Clean();
            if (!m.HasNormals || m.ContainsZeroLengthNormals())
            {
                m.GenerateVertexNormals();
            }
            m.NormalizeNormals();
            Mesh pc = new SurfacePointSampler().GenerateSampledMesh(m, targetNumberOfFaces / m.SurfaceArea());
            pc.HasUVs = false;
            // TODO: Why do we need to normalize here, issue with GenerateSampledMesh?
            pc.NormalizeNormals();
            Mesh reconstructed = PoissonReconstruction.PoissonReconstruct(pc); //FSSR.Reconstruct(pc);//
            reconstructed.Clean();
            List<Vertex> corners = null;
            if (cornerDirection.HasValue)
            {
                corners = reconstructed.Corners(cornerDirection.Value);
            }
            Mesh decimated = EdgeCollapse.QuadricEdgeCollapse(reconstructed, targetNumberOfFaces, perimeterPenaltyFactor: 20, notTouched: corners);
            decimated.Clean();
            decimated.GenerateVertexNormals();
            if (clippingBounds.HasValue)
            {
                decimated = Mesh.Clip(decimated, clippingBounds.Value);
            }
            return decimated;
        }
    }
}
