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
        private static readonly ILog logger = LogManager.GetLogger(typeof(FSSR));

        /// <summary>
        /// Creates a mesh from a point cloud
        /// </summary>
        /// <param name="pointCloud"></param>
        /// <returns></returns>
        public static Mesh PoissonReconstruct(Mesh pointCloud)
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
            string poissonReconExe = Path.Combine(PathHelper.GetApplicationPath(), "ExternalApps", "PoissonRecon.exe");
            Mesh result = null;
            float scale = MathE.Max(pointCloud.Bounds().Size().ToFloatArray()) / (float)Math.Sqrt(pointCloud.Vertices.Count) * 2;
            TemporaryFile.GetAndDelete(".ply", inputFile =>
            {
                PLYSerializer.Write(pointCloud, inputFile, new FSSRPlyWriter(scale));
                TemporaryFile.GetAndDelete(".ply", outputFile =>
                {
                    ProgramRunner pr = new ProgramRunner(poissonReconExe, "--in " + inputFile + " --out " + outputFile + " --scale 1", captureOutput: true);
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
