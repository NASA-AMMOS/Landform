using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using JPLOPS.Util;

namespace JPLOPS.Geometry
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
        ResampleFSSR, //MeshExtensions.ResampleDecimated(MeshReconstructionMethod.FSSR)
        ResamplePoisson //MeshExtensions.ResampleDecimated(MeshReconstructionMethod.Poisson)
    }

    public static class MeshExtensions
    {
        public const double EDGE_COLLAPSE_PERIMETER_FACTOR = 100;
        public const double DEF_SAMPLES_PER_FACE = 4;

        /// <summary>
        /// preserves/regenerates normals but loses colors and UVs
        /// </summary>
        public static Mesh Decimated(this Mesh m, int targetFaces,
                                     MeshDecimationMethod method = MeshDecimationMethod.ResampleFSSR,
                                     BoundingBox? clippingBounds = null, Vector3? upAxis = null,
                                     ILogger logger = null)
        {
            bool hadNormals = m.HasNormals;
            switch (method)
            {
                case MeshDecimationMethod.EdgeCollapse:
                {
                    List<Vertex> corners = upAxis.HasValue ? m.Corners(upAxis.Value) : null;
                    m = EdgeCollapse.QuadricEdgeCollapse(m, targetFaces,
                                                         perimeterPenaltyFactor: EDGE_COLLAPSE_PERIMETER_FACTOR,
                                                         notTouched: corners);
                    m.Clean();
                    if (clippingBounds.HasValue)
                    {
                        m.Clip(clippingBounds.Value);
                    }
                    break;
                }
                case MeshDecimationMethod.ResampleFSSR:
                {
                    m = ResampleDecimated(m, targetFaces, MeshReconstructionMethod.FSSR, clippingBounds, upAxis,
                                          logger: logger);
                    break;
                }
                case MeshDecimationMethod.ResamplePoisson:
                {
                    m = ResampleDecimated(m, targetFaces, MeshReconstructionMethod.Poisson, clippingBounds, upAxis,
                                          logger: logger);
                    break;
                }
                default: throw new Exception("unknown decimation method " + method);
            }
            if (!hadNormals)
            {
                m.HasNormals = false;
            }
            else if (!m.HasNormals)
            {
                m.GenerateVertexNormals();
            }
            return m;
        }

        /// <summary>
        /// sample points on mesh proportional to targetFaces with SurfacePointSampler
        /// then reconstruct mesh from those using indicated algorithm
        /// then run QuadricEdgeCollapse
        /// preserves/regenerates normals but loses colors and UVs
        /// </summary>
        public static Mesh ResampleDecimated(this Mesh m, int targetFaces,
                                             MeshReconstructionMethod method = MeshReconstructionMethod.FSSR,
                                             BoundingBox? clippingBounds = null, Vector3? upAxis = null,
                                             double samplesPerFace = DEF_SAMPLES_PER_FACE, ILogger logger = null)
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
            bool hadNormals = m.HasNormals;
            m = new Mesh(m); //make copy
            m.Clean();
            if (!m.HasNormals || m.ContainsInvalidNormals())
            {
                m.GenerateVertexNormals();
            }
            m.NormalizeNormals();
            double density = samplesPerFace * targetFaces / area;
            Mesh pc = new SurfacePointSampler().GenerateSampledMesh(m, density, area: area);
            if (logger != null)
            {
                logger.LogInfo("ResampleDecimated {0} src tris {1}, src area {2:F3}, {3:F3} samples/face, " +
                               "{4} target faces, {5:F3} density, {6} pts",
                               method, Fmt.KMG(m.Faces.Count), area, samplesPerFace, Fmt.KMG(targetFaces), density,
                               Fmt.KMG(pc.Vertices.Count));
            }
            pc.HasUVs = false;
            switch (method)
            {
                case MeshReconstructionMethod.FSSR:
                {
                    //double globalScale = area / pc.Vertices.Count;
                    double fudge = 4;
                    double globalScale = fudge / Math.Sqrt(2 * density); //https://mathoverflow.net/a/124740
                    m = FSSR.Reconstruct(pc, globalScale, logger: logger);
                    break;
                }
                case MeshReconstructionMethod.Poisson:
                {
                    m = PoissonReconstruction.Reconstruct(pc, logger: logger);
                    break;
                }
                default: throw new Exception("unknown mesh reconstruction method " + method);
            }
            m.Clean();
            if (m.Faces.Count > targetFaces)
            {
                if (logger != null)
                {
                    logger.LogInfo("ResampleDecimated {0} edge collapse {1} -> {2}",
                                   method, Fmt.KMG(m.Faces.Count), Fmt.KMG(targetFaces));
                }
                m = EdgeCollapse.QuadricEdgeCollapse(m, targetFaces,
                                                     perimeterPenaltyFactor: EDGE_COLLAPSE_PERIMETER_FACTOR,
                                                     notTouched: upAxis.HasValue ? m.Corners(upAxis.Value) : null);
                m.Clean();
            }
            else if (logger != null)
            {
                logger.LogInfo("ResampleDecimated {0} skipping edge collapse {1} <= {2}",
                               method, Fmt.KMG(m.Faces.Count), Fmt.KMG(targetFaces));
            }
            if (clippingBounds.HasValue)
            {
                m.Clip(clippingBounds.Value);
            }
            if (!hadNormals)
            {
                m.HasNormals = false;
            }
            else if (!m.HasNormals)
            {
                m.GenerateVertexNormals();
            }
            return m;
        }
    }
}
