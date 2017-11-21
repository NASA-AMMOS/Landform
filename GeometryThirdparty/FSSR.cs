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
    /// <summary>
    /// Class to support running FSSR
    /// Depends on bundled executables fssrecon.exe and meshclean.exe
    /// </summary>
    public class FSSR
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(FSSR));



        /// <summary>
        /// Build a mesh from the provided point cloud or mesh with faces
        /// Requires the mesh has normals but not uvs or colors
        /// Returns a mesh with normals
        /// </summary>
        /// <param name="pointCloud"></param>
        /// <returns></returns>
        public static Mesh Reconstruct(Mesh pointCloud)
        {
            if(pointCloud.Vertices.Count == 0)
            {
                throw new FSSRException("Empty point cloud passed into FSSR");
            }
            if(!pointCloud.HasNormals)
            {
                throw new FSSRException("FSSR requires normals");
            }
            if (pointCloud.HasUVs)
            {
                throw new FSSRException("FSSR meshes cannot have uvs");
            }
            if (pointCloud.HasColors)
            {
                throw new FSSRException("FSSR meshes cannot have colors");
            }
            string fssrExe = Path.Combine(PathHelper.GetApplicationPath(), "ExternalApps", "fssrecon.exe");
            string cleanExe = Path.Combine(PathHelper.GetApplicationPath(), "ExternalApps", "meshclean.exe");

            Mesh result = null;
            float scale = MathE.Max(pointCloud.Bounds().Size().ToFloatArray()) / (float)Math.Sqrt(pointCloud.Vertices.Count) * 2;
            TemporaryFile.GetAndDelete(".ply", inputFile => {
                PLYSerializer.Write(pointCloud, inputFile, new FSSRPlyWriter(scale));
                TemporaryFile.GetAndDelete(".ply", outputFile => {
                    ProgramRunner pr = new ProgramRunner(fssrExe, inputFile + " " + outputFile, captureOutput: true);
                    pr.Run();
                    if(!File.Exists(outputFile))
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
                    TemporaryFile.GetAndDelete(".ply", cleanFile => {
                        int minVertsPerComponent = (int)Math.Max(ouputVertCount * 0.05f, 5);
                        ProgramRunner pr2 = new ProgramRunner(cleanExe, "-c " + minVertsPerComponent + " " + outputFile + " " + cleanFile, captureOutput: true);
                        pr2.Run();
                        if (!File.Exists(cleanFile) || Mesh.Load(cleanFile).Vertices.Count == 0)
                        {
                            logger.Error(pr2.OutputText);
                            logger.Error(pr2.ErrorText);
                            cleanFile = outputFile;
                        }
                        result = Mesh.Load(cleanFile);
                        if(result.Vertices.Count == 0)
                        {
                            throw new FSSRException("Failed to reconstruct mesh");
                        }
                    });
                });
            });
            result = MeshLab.ComputeNormals(result);
            return result;
        }

        /// <summary>
        /// Decimate a mesh by resampling it and then reconstructing it using FSSR
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="numSamples"></param>
        /// <param name="targetFaces"></param>
        /// <returns></returns>
        public static Mesh ResampleDeimation(Mesh mesh, int numSamples, int targetFaces)
        {
            Mesh points = MeshLab.Sample(mesh, numSamples);
            Mesh surface = Reconstruct(points);
            Mesh decimated = MeshLab.Decimate(surface, targetFaces);
            return decimated;
        }
    }
}
