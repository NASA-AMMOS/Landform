using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;
using OPS.MathExtensions;
using OPS.Util;
using OPS.Imaging;

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
        public static Mesh Reconstruct(Mesh pointCloud, float? scale = null)
        {
            if (pointCloud.Vertices.Count == 0)
            {
                throw new FSSRException("Empty point cloud passed into FSSR");
            }
            if (!pointCloud.HasNormals)
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
            if (!scale.HasValue)
            {
                scale = MathE.Max(pointCloud.Bounds().Extent().ToFloatArray()) /
                    (float)Math.Sqrt(pointCloud.Vertices.Count) * 2;
            }
            TemporaryFile.GetAndDelete(".ply", inputFile => {
                PLYSerializer.Write(pointCloud, inputFile, new FSSRPlyWriter(scale.Value));
                TemporaryFile.GetAndDelete(".ply", outputFile => {
                    ProgramRunner pr = new ProgramRunner(fssrExe, inputFile + " " + outputFile, captureOutput: true);
                    pr.Run();
                    int ouputVertCount = Mesh.Load(outputFile).Vertices.Count;
                    if (!File.Exists(outputFile) || ouputVertCount == 0)
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
            result.Clean();
            result.GenerateVertexNormals();
            return result;
        }

        /// <summary>
        /// build a mesh with FSSR reconstruction from an organized point cloud
        /// normals image must be supplied
        /// if mask image is provided then any pixels which are 0 there are ignored
        /// </summary>
        public static Mesh Reconstruct(Image points, Image normals, Image mask = null)
        {
            if (normals == null)
            {
                throw new ArgumentException("FSSR reconstruction requires normals");
            }

            return Reconstruct(OrganizedPointCloud.BuildPointCloudMesh(points, normals, mask));            
        }
    }
}
