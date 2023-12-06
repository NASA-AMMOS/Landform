using System;
using System.IO;
using JPLOPS.Util;
using JPLOPS.Imaging;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Floating scale surface reconstruction
    /// Simon Fuhrmann, Michael Goesele
    /// ACM Transactions on GraphicsJuly 2014 Article No.: 46
    /// https://github.com/pmoulon/fssr
    /// https://www.gcc.tu-darmstadt.de/media/gcc/papers/Fuhrmann-2014-FSS.pdf
    /// Class to support running FSSR
    /// Depends on bundled executables fssrecon.exe and meshclean.exe
    /// </summary>
    public class FSSR
    {
        public const double DEF_ENLARGE_PIXEL_SCALE = 2;

        /// <summary>
        /// Build a mesh from the provided point cloud or mesh with faces
        /// Requires the mesh has normals but not uvs or colors
        /// Returns a mesh with normals
        /// </summary>
        public static Mesh Reconstruct(Mesh pointCloud, double globalScale = -1,
                                       bool useNormalLengthAsVertexScale = false,
                                       Action<Mesh> uncleanedMesh = null, bool runClean = true,
                                       ILogger logger = null)
        {
            if (pointCloud.Vertices.Count < 3)
            {
                throw new MeshException("FSSR requires at least 3 vertices");
            }
            if (!pointCloud.HasNormals)
            {
                throw new MeshException("FSSR requires normals");
            }
            int nr = pointCloud.RemoveInvalidPoints();
            if (nr > 0)
            {
                if (logger != null) logger.LogWarn("FSSR input had invalid points, removed {0} points", nr);
                if (pointCloud.Vertices.Count < 3)
                {
                    throw new MeshException("FSSR requires at least 3 vertices");
                }
            }
            nr = pointCloud.RemoveInvalidNormals();
            if (nr > 0)
            {
                if (logger != null) logger.LogWarn("FSSR input had invalid normals, removed {0} points", nr);
                if (pointCloud.Vertices.Count < 3)
                {
                    throw new MeshException("FSSR requires at least 3 vertices");
                }
            }
            if (pointCloud.HasUVs)
            {
                if (logger != null) logger.LogWarn("FSSR meshes cannot have UVs - removing");
                pointCloud.HasUVs = false;
            }
            if (pointCloud.HasColors)
            {
                if (logger != null) logger.LogWarn("FSSR meshes cannot have colors - removing");
                pointCloud = new Mesh(pointCloud);
                pointCloud.ClearColors();
            }

            if (globalScale <= 0 && !useNormalLengthAsVertexScale)
            {
                //this is a very rough method
                //see MeshExtensions.ResampleDecimated() for a more tuned calculation when the mesh area is available
                double maxDim = pointCloud.Bounds().MaxDimension();
                globalScale = DEF_ENLARGE_PIXEL_SCALE * (maxDim / Math.Sqrt(pointCloud.Vertices.Count));
                if (logger != null)
                {
                    logger.LogInfo("computed global scale {0} for point cloud with max bound {1}m and {2} vertices",
                                   globalScale, maxDim, pointCloud.Vertices.Count);
                }
            }
            else if (logger != null)
            {
                if (useNormalLengthAsVertexScale)
                {
                    logger.LogInfo("using point cloud normal lengths as individual vertex scale values");
                }
                else
                {
                    logger.LogInfo("using global scale value {0}", globalScale);
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
                    if (logger != null) logger.LogVerbose("running command: {0} {1}", fssrExe, arguments);
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

                    if (logger != null)
                    {
                        logger.LogInfo("reconstructed mesh has {0} faces", Fmt.KMG(result.Faces.Count));
                    }
                }
                catch (Exception ex)
                {
                    if (logger != null)
                    {
                        logger.LogError(pr.OutputText);
                        logger.LogError(pr.ErrorText);
                    }
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
                        if (logger != null) logger.LogVerbose("running command: {0} {1}", cleanExe, arguments);
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
                        
                        if (logger != null)
                        {
                            logger.LogInfo("cleaned mesh has {0} faces", Fmt.KMG(result.Faces.Count));
                        }
                    }
                    catch (Exception ex)
                    {
                        if (logger != null)
                        {
                            logger.LogError(pr.OutputText);
                            logger.LogError(pr.ErrorText);
                        }
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
