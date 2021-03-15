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
        public const double DEF_ENLARGE_PIXEL_SCALE = 2;

        private static readonly ILog logger = LogManager.GetLogger(typeof(FSSR));

        /// <summary>
        /// Build a mesh from the provided point cloud or mesh with faces
        /// Requires the mesh has normals but not uvs or colors
        /// Returns a mesh with normals
        /// </summary>
        public static Mesh Reconstruct(Mesh pointCloud, double globalScale = -1,
                                       bool useNormalLengthAsVertexScale = false,
                                       Action<Mesh> uncleanedMesh = null, bool runClean = true,
                                       bool quiet = true)
        {
            if (pointCloud.Vertices.Count < 3)
            {
                throw new MeshException("FSSR requires at least 3 vertices");
            }
            if (!pointCloud.HasNormals)
            {
                throw new MeshException("FSSR requires normals");
            }
            if (pointCloud.ContainsZeroLengthNormals())
            {
                logger.Warn("FSSR input mesh had zero length normals - removing");
                pointCloud.RemoveZeroLengthNormals();
                if (pointCloud.Vertices.Count < 3)
                {
                    throw new MeshException("FSSR requires at least 3 vertices");
                }
            }
            if (pointCloud.HasUVs)
            {
                logger.Warn("FSSR meshes cannot have UVs - removing");
                pointCloud.HasUVs = false;
            }
            if (pointCloud.HasColors)
            {
                logger.Warn("FSSR meshes cannot have colors - removing");
                pointCloud = new Mesh(pointCloud);
                pointCloud.ClearColors();
            }

            if (globalScale <= 0 && !useNormalLengthAsVertexScale)
            {
                double maxDim = pointCloud.Bounds().MaxDimension();
                globalScale = DEF_ENLARGE_PIXEL_SCALE * (maxDim / Math.Sqrt(pointCloud.Vertices.Count));
                if (!quiet)
                {
                    logger.InfoFormat("computed global scale {0} for point cloud with max bound {1}m and {2} vertices",
                                      globalScale, maxDim, pointCloud.Vertices.Count);
                }
            }
            else if (!quiet)
            {
                if (useNormalLengthAsVertexScale)
                {
                    logger.InfoFormat("using point cloud normal lengths as individual vertex scale values");
                }
                else
                {
                    logger.InfoFormat("using global scale value {0}", globalScale);
                }
            }

            string fssrExe = Path.Combine(PathHelper.GetApplicationPath(), "ExternalApps", "fssrecon.exe");
            string cleanExe = Path.Combine(PathHelper.GetApplicationPath(), "ExternalApps", "meshclean.exe");

            Mesh result = null;

            TemporaryFile.GetAndDeleteMultiple(3, ".ply", files => {

                string inputFile = files[0];
                string outputFile = files[1];
                string cleanFile = files[2];

                var writer = useNormalLengthAsVertexScale ?
                    new FSSRPLYWriter(true) : new FSSRPLYWriter(globalScale);
                PLYSerializer.Write(pointCloud, inputFile, writer);

                string arguments = inputFile + " " + outputFile;
                ProgramRunner pr = new ProgramRunner(fssrExe, arguments, captureOutput: true);
                try
                {
                    if (!quiet)
                    {
                        logger.InfoFormat("running command: {0} {1}", fssrExe, arguments);
                    }
                    int exitCode = pr.Run();

                    if (exitCode != 0)
                    {
                        throw new MeshException("FSSR exited with status " + exitCode);
                    }
                    
                    if (File.Exists(outputFile))
                    {
                        result = Mesh.Load(outputFile);
                    }
                    else
                    {
                        throw new MeshException("FSSR no output file");
                    }
                    
                    if (result.Vertices.Count == 0 || result.Faces.Count == 0)
                    {
                        throw new MeshException("FSSR empty output");
                    }

                    if (!quiet)
                    {
                        logger.InfoFormat("reconstructed mesh has {0} faces", Fmt.KMG(result.Faces.Count));
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(pr.OutputText);
                    logger.Error(pr.ErrorText);
                    throw new MeshException("failed to run " + fssrExe + " " + arguments + ": " + ex.Message);
                }

                if (uncleanedMesh != null)
                {
                    uncleanedMesh(result);
                }

                if (runClean)
                {
                    int minVertsPerComponent = (int)Math.Max(result.Vertices.Count * 0.05f, 5);
                    arguments = "-c " + minVertsPerComponent + " " + outputFile + " " + cleanFile;
                    pr = new ProgramRunner(cleanExe, arguments, captureOutput: true);
                    try
                    {
                        if (!quiet)
                        {
                            logger.InfoFormat("running command: {0} {1}", cleanExe, arguments);
                        }
                        int exitCode = pr.Run();
                        
                        if (exitCode != 0)
                        {
                            throw new MeshException("FSSR clean exited with status " + exitCode);
                        }
                        
                        if (File.Exists(cleanFile))
                        {
                            result = Mesh.Load(cleanFile);
                        }
                        else
                        {
                            throw new MeshException("FSSR clean no output file");
                        }
                        
                        if (result.Vertices.Count == 0 || result.Faces.Count == 0)
                        {
                            throw new MeshException("FSSR clean empty output");
                        }
                        
                        if (!quiet)
                        {
                            logger.InfoFormat("cleaned mesh has {0} faces", Fmt.KMG(result.Faces.Count));
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(pr.OutputText);
                        logger.Error(pr.ErrorText);
                        throw new MeshException("failed to run " + cleanExe + " " + arguments + ": " + ex.Message);
                    }
                }
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
        public static Mesh Reconstruct(Image points, Image normals, Image mask = null,
                                       bool useNormalLengthAsVertexScale = false)
        {
            if (normals == null)
            {
                throw new ArgumentException("FSSR reconstruction requires normals");
            }
            return Reconstruct(OrganizedPointCloud.BuildPointCloudMesh(points, normals, mask),
                               useNormalLengthAsVertexScale: useNormalLengthAsVertexScale);
        }
    }

    public class FSSRPLYWriter : PLYMaximumCompatibilityWriter
    {
        private readonly double scale;

        public FSSRPLYWriter(bool writeNormalLengthsAsValue)
            : base(writeNormalLengthsAsValue: writeNormalLengthsAsValue)
        {
            scale = 0;
        }

        public FSSRPLYWriter(double scale)
        {
            this.scale = scale;
        }
        
        protected override void WriteVertexStructureHeader(Mesh m, StreamWriter sw)
        {
            base.WriteVertexStructureHeader(m, sw);
            if (scale > 0)
            {
                sw.WriteLine("property float value"); // scale value
            }
        }

        public override void WriteVertex(Mesh m, Vertex v, Stream s)
        {
            base.WriteVertex(m, v, s);
            if (scale > 0)
            {
                WriteFloatValue((float)scale, s);
            }
        }
    }
}
