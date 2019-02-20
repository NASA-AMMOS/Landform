using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{

    public enum MeshReconMethod
    {
        FSSR,
        Poisson
    }

    public static class MeshExtensions
    {

        public static Mesh ResampleDecimation(this Mesh m, MeshReconMethod reconstructionMethod, int targetNumberOfFaces = 2000, BoundingBox? clippingBounds = null, Vector3? cornerDirection = null)
        {
            // First make a copy
            m = new Mesh(m);
            m.Clean();
            if (!m.HasNormals || m.ContainsZeroLengthNormals())
            {
                m.GenerateVertexNormals();
            }
            m.NormalizeNormals();
            double density = (targetNumberOfFaces / m.SurfaceArea()) * 4;
            Mesh pc = new SurfacePointSampler().GenerateSampledMesh(m, density); pc.HasUVs = false;
            // TODO: Why do we need to normalize here, issue with GenerateSampledMesh?
            pc.NormalizeNormals();
            Mesh reconstructed = null;
            if (reconstructionMethod == MeshReconMethod.FSSR)
            {
                reconstructed = FSSR.Reconstruct(pc);
            }
            else if (reconstructionMethod == MeshReconMethod.Poisson)
            {
                reconstructed = PoissonReconstruction.Reconstruct(pc);
            }
            else
            {
                throw new Exception("Unknown mesh reconstruction method");
            }
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
