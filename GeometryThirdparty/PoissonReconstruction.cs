using OPS.MathExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using System.IO;
using log4net;

namespace OPS.Geometry
{
    public class PoissonReconstruction
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(PoissonReconstruction));
        public enum BoundaryTypes { Free = 1, Dirichlet = 2, Neumann = 3 };

        public class Options
        {
            public BoundaryTypes Boundary;          //exe defaults: Neumann
            public float MinOctreeCellWidthMeters;  //exe defaults: doesn't use this parameter, uses --depth 8
            public float MinOctreeSamplesPerCell;   //exe defaults: 1, recommends 1-5 clean data 15-20 noisy data
            public int BSplineDegree;               //exe defaults: 1, supports 1-2
            public bool UseNormalsForConfidence;    //exe defaults: no, if true magnitude of normal indicates confidence
        };

        /// <summary>
        /// Creates a mesh from a point cloud
        /// </summary>
        /// <param name="pointCloud"></param>
        /// <param name="extrapolateBoundaries">if true, it uses the neumann boundary (derivative). if false it uses the dirichlet boundary (actual value). the former can add big sweeping wings to the edges of the mesh, but does more agressive hole filling.</param>
        /// <returns></returns>
        public static Mesh Reconstruct(Mesh pointCloud, Options options=null)
        {
            if (pointCloud.Vertices.Count == 0)
            {
                throw new MeshException("Empty point cloud passed into PoissonRecon");
            }
            if (!pointCloud.HasNormals)
            {
                throw new MeshException("PoissonRecon requires normals");
            }
            if (pointCloud.HasUVs)
            {
                throw new MeshException("PoissonRecon meshes cannot have uvs");
            }
            if (pointCloud.HasColors)
            {
                throw new MeshException("PoissonRecon meshes cannot have colors");
            }
            // Confirm all normals are non-zero
            if (pointCloud.ContainsZeroLengthNormals())
            {
                throw new MeshException("PoissonRecon input mesh had invalid normals");
            }

            //unless using normal magnitude for confidence
            if (options != null && !options.UseNormalsForConfidence)
            {
                int notNormalCount = 0;
                foreach (var vert in pointCloud.Vertices)
                {
                    double len = vert.Normal.Length();
                    if (Math.Abs(len - 1) > 1e-3)
                    {
                        notNormalCount++;
                    }
                }
                if (notNormalCount > 0)
                {
                    logger.Warn("Found " + notNormalCount + " vertices with non unit length normals");
                }
            }

            string poissonReconExe = Path.Combine(PathHelper.GetApplicationPath(), "ExternalApps", "PoissonReconV10.02.exe");
            Mesh result = null;
            float scale = MathE.Max(pointCloud.Bounds().Size().ToFloatArray()) / (float)Math.Sqrt(pointCloud.Vertices.Count) * 2;
            TemporaryFile.GetAndDelete(".ply", inputFile =>
            {
                PLYSerializer.Write(pointCloud, inputFile, new PLYMaximumCompatibilityWriter(false));
                TemporaryFile.GetAndDelete(".ply", outputFile =>
                {
                    string arguments = "--in " + inputFile + " --out " + outputFile + " --normals";

                    if (options != null)
                    {
                        arguments += String.Format(" --bType {0} --width {1} --samplesPerNode {2} --degree {3} --confidence {4}", (int)options.Boundary, options.MinOctreeCellWidthMeters, options.MinOctreeSamplesPerCell, options.BSplineDegree, options.UseNormalsForConfidence ? 1 : 0);
                    }
                    ProgramRunner pr = new ProgramRunner(poissonReconExe, arguments, captureOutput: true);
                    pr.Run();
                    if (!File.Exists(outputFile))
                    {
                        logger.Error(pr.OutputText);
                        logger.Error(pr.ErrorText);
                    }
                    int ouputVertCount = Mesh.Load(outputFile).Vertices.Count;
                    if (ouputVertCount == 0)
                    {
                        logger.Error(pr.OutputText);
                        logger.Error(pr.ErrorText);
                    }
                    result = Mesh.Load(outputFile);
                    if (result.Vertices.Count == 0)
                    {
                        throw new MeshException("Failed to reconstruct mesh");
                    }
                });
            });
            return result;
        }
    }
}
