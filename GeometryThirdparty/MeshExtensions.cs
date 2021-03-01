using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace OPS.Geometry
{
    public enum MeshReconstructionMethod
    {
        FSSR,
        Poisson,
        Organized
    }

    public enum MeshDecimationMethod
    {
        EdgeCollapse, //EdgeCollapse.QuadricEdgeCollapse()
        ResampleFSSR, //MeshExtensions.ResampleDecimation(MeshReconstructionMethod.FSSR)
        ResamplePoisson, //MeshExtensions.ResampleDecimation(MeshReconstructionMethod.Poisson)
        MeshLab, //MeshLab.Decimate()
        MeshLabResample //MeshLab.ResampleDecimation()
    }

    public static class MeshExtensions
    {
        public const double EDGE_COLLAPSE_PERIMETER_FACTOR = 20;
        public const int SAMPLES_PER_FACE = 4;

        /// <summary>
        /// preserves (or possibly adds) normals but loses colors and UVs
        /// </summary>
        public static Mesh Decimate(this Mesh m, int targetFaces,
                                    MeshDecimationMethod method = MeshDecimationMethod.ResampleFSSR,
                                    BoundingBox? clippingBounds = null, Vector3? upAxis = null)
        {
            switch (method)
            {
                case MeshDecimationMethod.EdgeCollapse:
                {
                    bool hadNormals = m.HasNormals;
                    List<Vertex> corners = null;
                    if (upAxis.HasValue)
                    {
                        corners = m.Corners(upAxis.Value);
                    }
                    m = EdgeCollapse.QuadricEdgeCollapse(m, targetFaces,
                                                         perimeterPenaltyFactor: EDGE_COLLAPSE_PERIMETER_FACTOR,
                                                         notTouched: corners);
                    m.Clean();
                    if (hadNormals)
                    {
                        m.GenerateVertexNormals();
                    }
                    if (clippingBounds.HasValue)
                    {
                        m = Mesh.Clip(m, clippingBounds.Value);
                    }
                    return m;
                }
                case MeshDecimationMethod.ResampleFSSR:
                {
                    return ResampleDecimation(m, targetFaces, MeshReconstructionMethod.FSSR, clippingBounds, upAxis);
                }
                case MeshDecimationMethod.ResamplePoisson:
                {
                    return ResampleDecimation(m, targetFaces, MeshReconstructionMethod.Poisson, clippingBounds, upAxis);
                }
                case MeshDecimationMethod.MeshLab:
                {
                    m = MeshLab.Decimate(m, targetFaces);
                    if (clippingBounds.HasValue)
                    {
                        m = Mesh.Clip(m, clippingBounds.Value);
                    }
                    return m;
                }
                case MeshDecimationMethod.MeshLabResample:
                {
                    m = MeshLab.ResampleDecimation(m, SAMPLES_PER_FACE * targetFaces, targetFaces);
                    if (clippingBounds.HasValue)
                    {
                        m = Mesh.Clip(m, clippingBounds.Value);
                    }
                    return m;
                }
                default: throw new Exception("unknown decimation method " + method);
            }
        }

        /// <summary>
        /// sample points on mesh proportional to targetFaces with SurfacePointSampler
        /// then reconstruct mesh from those using indicated algorithm
        /// then run QuadricEdgeCollapse
        /// preserves or adds normals but loses colors and UVs
        /// </summary>
        public static Mesh ResampleDecimation(this Mesh m, int targetFaces,
                                              MeshReconstructionMethod method = MeshReconstructionMethod.FSSR,
                                              BoundingBox? clippingBounds = null, Vector3? upAxis = null)
        {
            if (method != MeshReconstructionMethod.FSSR && method != MeshReconstructionMethod.Poisson)
            {
                throw new ArgumentException("unsupported reconstruction method: " + method);
            }
            m = new Mesh(m); //make copy
            m.Clean();
            if (!m.HasNormals || m.ContainsZeroLengthNormals())
            {
                m.GenerateVertexNormals();
            }
            m.NormalizeNormals();
            double density = SAMPLES_PER_FACE * targetFaces / m.SurfaceArea();
            Mesh pc = new SurfacePointSampler().GenerateSampledMesh(m, density);
            pc.HasUVs = false;
            switch (method)
            {
                case MeshReconstructionMethod.FSSR:
                {
                    m = FSSR.Reconstruct(pc);
                    break;
                }
                case MeshReconstructionMethod.Poisson:
                {
                    m = PoissonReconstruction.Reconstruct(pc);
                    break;
                }
                default: throw new Exception("unknown mesh reconstruction method " + method);
            }
            m.Clean();
            List<Vertex> corners = null;
            if (upAxis.HasValue)
            {
                corners = m.Corners(upAxis.Value);
            }
            m = EdgeCollapse.QuadricEdgeCollapse(m, targetFaces, perimeterPenaltyFactor: EDGE_COLLAPSE_PERIMETER_FACTOR,
                                                 notTouched: corners);
            m.Clean();
            m.GenerateVertexNormals();
            if (clippingBounds.HasValue)
            {
                m = Mesh.Clip(m, clippingBounds.Value);
            }
            return m;
        }
    }
}
