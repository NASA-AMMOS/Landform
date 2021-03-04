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
        public const double EDGE_COLLAPSE_PERIMETER_FACTOR = 100;
        public const double DEF_SAMPLES_PER_FACE = 4;

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
                        m.Clip(clippingBounds.Value);
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
                        m.Clip(clippingBounds.Value);
                    }
                    return m;
                }
                case MeshDecimationMethod.MeshLabResample:
                {
                    m = MeshLab.ResampleDecimation(m, (int)(DEF_SAMPLES_PER_FACE * targetFaces), targetFaces);
                    if (clippingBounds.HasValue)
                    {
                        m.Clip(clippingBounds.Value);
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
                                              BoundingBox? clippingBounds = null, Vector3? upAxis = null,
                                              double samplesPerFace = DEF_SAMPLES_PER_FACE)
        {
            double area = m.SurfaceArea();
            if (area < 1e-10)
            {
                return m;
            }
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
            double density = samplesPerFace * targetFaces / area;
            Mesh pc = new SurfacePointSampler().GenerateSampledMesh(m, density, area: area);
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
            if (m.Faces.Count > targetFaces)
            {
                //Console.WriteLine("edge collapse {0} -> {1}", m.Faces.Count, targetFaces);
                m = EdgeCollapse.QuadricEdgeCollapse(m, targetFaces,
                                                     perimeterPenaltyFactor: EDGE_COLLAPSE_PERIMETER_FACTOR,
                                                     notTouched: upAxis.HasValue ? m.Corners(upAxis.Value) : null);
                m.Clean();
            }
            //else Console.WriteLine("skipping edge collapse {0} <= {1}", m.Faces.Count, targetFaces);
            m.GenerateVertexNormals();
            if (clippingBounds.HasValue)
            {
                m.Clip(clippingBounds.Value);
            }
            return m;
        }
    }
}
