using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.Imaging;

namespace JPLOPS.Geometry
{
    public class PoissonConfig : SingletonConfig<PoissonConfig>
    {
        [ConfigEnvironmentVariable("LANDFORM_POISSON_EXE")]
        public string PoissonExe { get; set; } = "PoissonRecon.V13.72.exe";

        [ConfigEnvironmentVariable("LANDFORM_POISSON_TRIMMER_EXE")]
        public string TrimmerExe { get; set; } = "SurfaceTrimmer.V13.72.exe";

        [ConfigEnvironmentVariable("LANDFORM_POISSON_EXE_LEGACY")]
        public bool PoissonExeLegacy { get; set; }
    }


    /// Poisson Surface Reconstruction
    /// Michael Kazhdan, Matthew Bolitho, Hugues Hoppe
    /// Eurographics Symposium on Geometry Processing (2006)
    /// http://www.cs.jhu.edu/~misha/MyPapers/SGP06.pdf
    /// http://www.cs.jhu.edu/~misha/Code/PoissonRecon
    public class PoissonReconstruction
    {
        public enum BoundaryType {
            Free = 1,
            Dirichlet = 2, //constrain finite element boundary to surface position
            Neumann = 3 //(exe default) constrain finite element boundary to surface normal
        };

        public const PoissonReconstruction.BoundaryType
            DEF_BOUNDARY_TYPE = PoissonReconstruction.BoundaryType.Dirichlet;

        public const int DEF_OCTREE_DEPTH = 0;
        public const double DEF_MIN_OCTREE_CELL_WIDTH_METERS = 0.10;
        public const int DEF_MIN_OCTREE_SAMPLES_PER_CELL = 15;
        public const int DEF_BSPLINE_DEGREE = 1;
        public const double DEF_CONFIDENCE_EXPONENT = 1;
        public const double DEF_TRIMMER_LEVEL = 9;
        public const double DEF_TRIMMER_LEVEL_LENIENT = 8;
        public const bool DEF_PASS_ENVELOPE_TO_POISSON = false;
        public const bool DEF_CLIP_TO_ENVELOPE = true;
        public const double DEF_MIN_ISLAND_RATIO = 0.2;

        public class Options
        {
            //exe defaults: Neumann
            public BoundaryType Boundary = DEF_BOUNDARY_TYPE;

            //exe defaults: --depth 8, mutually exclusive with MinOctreeCellWidthMeters
            public int OctreeDepth = DEF_OCTREE_DEPTH;

            //exe defaults: default doesn't use this parameter
            public double MinOctreeCellWidthMeters = DEF_MIN_OCTREE_CELL_WIDTH_METERS;

            //exe defaults: 1, recommends 1-5 clean data 15-20 noisy data
            public double MinOctreeSamplesPerCell = DEF_MIN_OCTREE_SAMPLES_PER_CELL;

            //exe defaults: 1
            public int BSplineDegree = DEF_BSPLINE_DEGREE;

            //exe defaults: 0, if > 0 then apply this exponent to the length of normals and use as confidence
            public double ConfidenceExponent = 0; 

            //exe defaults: 7, if tree level for density is less than amount, remove (higher number == more culling)
            public double TrimmerLevel = DEF_TRIMMER_LEVEL;

            //envelope bounding box
            public BoundingBox? Envelope = null;

            //whether to actually pass envelope to Poisson
            //requires V13+
            //also, this seems to end up making a tight bubble of extra flab
            //that the trimmer has a hard time getting rid of
            public bool PassEnvelopeToPoisson = DEF_PASS_ENVELOPE_TO_POISSON;

            //clip to envelope, if any, after reconstruction but before surface trimming
            public bool ClipToEnvelope = DEF_CLIP_TO_ENVELOPE;

            //after reconstruction but before surface trimming
            //remove islands whose bounding box diameter is less than this ratio
            //of the max island bounding box diameter
            public double MinIslandRatio = DEF_MIN_ISLAND_RATIO;

            public bool PreserveInputsOnError = false;
            public string PreserveInputsOverrideFolder = null;
            public string PreserveInputsOverrideName = null;
        };

        /// <summary>
        /// build a mesh with Poisson reconstruction from the given organized point cloud
        /// normals image must be supplied
        /// if mask image is provided then any pixels which are 0 there are ignored
        /// </summary>
        public static Mesh Reconstruct(Image points, Image normals, Image mask = null,
                                       bool normalsAreScaledByConfidence = false)
        {
            if (normals == null)
            {
                throw new ArgumentException("Poission reconstruction requires normals");
            }

            var opts = new Options();
            opts.ConfidenceExponent = normalsAreScaledByConfidence ? DEF_CONFIDENCE_EXPONENT : 0;

            return Reconstruct(OrganizedPointCloud.BuildPointCloudMesh(points, normals, mask), opts);
        }

        public static Mesh Reconstruct(Mesh pointCloud, Options options = null,
                                       Action<string> rawReconstructedMeshFile = null,
                                       Action<Mesh> untrimmedMeshWithValueScaledNormals = null,
                                       ILogger logger = null)
        {
            var cfg = PoissonConfig.Instance;
            string reconstructExe = Path.Combine(PathHelper.GetApplicationPath(), "ExternalApps", cfg.PoissonExe);

            if (pointCloud.Vertices.Count < 3)
            {
                throw new MeshException("Poisson requires at least 3 vertices");
            }
            if (!pointCloud.HasNormals)
            {
                throw new MeshException("Poisson requires normals");
            }
            int nr = pointCloud.RemoveInvalidPoints();
            if (nr > 0)
            {
                if (logger != null) logger.LogWarn("Poisson input had invalid points, removed {0} points", nr);
                if (pointCloud.Vertices.Count < 3)
                {
                    throw new MeshException("Poisson requires at least 3 vertices");
                }
            }
            nr = pointCloud.RemoveInvalidNormals();
            if (nr > 0)
            {
                if (logger != null) logger.LogWarn("Poisson input had invalid normals, removed {0} points", nr);
                if (pointCloud.Vertices.Count < 3)
                {
                    throw new MeshException("Poisson requires at least 3 vertices");
                }
            }
            if (pointCloud.HasUVs)
            {
                if (logger != null) logger.LogWarn("Poisson meshes cannot have UVs - removing");
                pointCloud.HasUVs = false;
            }
            if (pointCloud.HasColors && cfg.PoissonExeLegacy)
            {
                if (logger != null) logger.LogWarn("Poission (legacy) meshes cannot have colors - removing");
                pointCloud = new Mesh(pointCloud);
                pointCloud.ClearColors();
            }

            if (options == null || options.ConfidenceExponent == 0)
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
                if (notNormalCount > 0 && logger != null)
                {
                    logger.LogWarn("Poisson input has {0} non-unit normals, but not using normals for confidence",
                                   notNormalCount);
                }
            }

            var plyWriter = new PLYMaximumCompatibilityWriter();
            string inputFile = null, envFile = null;

            Mesh result = null;
            TemporaryFile.GetAndDeleteMultiple(3, ".ply", files =>
            {
                TemporaryFile.GetAndDeleteDirectory(tmpDir =>
                {
                    inputFile = files[0];
                    string outputFile = files[2];

                    PLYSerializer.Write(pointCloud, inputFile, plyWriter);
                    
                    string arguments = "--in " + inputFile + " --out " + outputFile;
                    
                    if(options.OctreeDepth != 0 && options.MinOctreeCellWidthMeters != 0.0)
                    {
                        throw new MeshException("OctreeDepth and MinOctreeCellWidthMeters are mutually exclusive");
                    }
                    else if (options.OctreeDepth == 0 && options.MinOctreeCellWidthMeters == 0)
                    {
                        throw new MeshException("either OctreeDepth and MinOctreeCellWidthMeters must be specified");
                    }
                    
                    if (!cfg.PoissonExeLegacy)
                    {
                        if (pointCloud.HasColors)
                        {
                            arguments += " --colors";
                        }
                        
                        arguments += " --normals 2"; //emit normals from solver: 1 = sample normals, 2 = gradients
                        arguments += " --tempDir " + tmpDir;
                        
                        if (options != null)
                        {
                            if (options.Envelope.HasValue)
                            {
                                envFile = files[1];
                                PLYSerializer.Write(options.Envelope.Value.ToMesh(), envFile, plyWriter);
                            }
                            
                            arguments +=
                                String.Format(" --bType {0} --samplesPerNode {1} --degree {2}{3}{4}{5}{6}",
                                              (int)options.Boundary, //0
                                              options.MinOctreeSamplesPerCell, //1
                                              options.BSplineDegree, //2
                                              options.MinOctreeCellWidthMeters > 0 ? //3
                                              (" --width " + options.MinOctreeCellWidthMeters) : "",
                                              options.OctreeDepth > 0 ? //4
                                              (" --depth " + options.OctreeDepth) : "",
                                              options.ConfidenceExponent != 0 ? //5
                                              (" --confidence " + options.ConfidenceExponent) : "",
                                              options.TrimmerLevel > 0 ? " --density" : ""); //6

                            if (options.Envelope.HasValue && options.PassEnvelopeToPoisson) //V13+
                            {
                                arguments += " --envelope " + envFile;
                            }
                        }
                    
                        //a workaround for running on powerful machines. without it there is an ERROR about not
                        // being able to open a file (likely a bug in multithread buffered file reading)
                        //arguments += " --threads 1";
                        arguments += " --threads " + CoreLimitedParallel.GetMaxDegreeOfParallelism();
                    }

                    ProgramRunner pr = new ProgramRunner(reconstructExe, arguments, captureOutput: true);
                    try
                    {
                        if (logger != null)
                        {
                            logger.LogInfo("running command: {0} {1}", reconstructExe, arguments);
                        }
                        int exitCode = pr.Run();
                        
                        if (exitCode != 0)
                        {
                            throw new MeshException("Poisson exited with status " + exitCode);
                        }
                        
                        //at least some legacy versions of PoissonRecon.exe can error out but still
                        //have zero exit code and write a valid and nonempty output mesh
                        //it seems the only way to detect that is like this
                        if (cfg.PoissonExeLegacy && !string.IsNullOrEmpty(pr.ErrorText) &&
                            !Regex.Split(pr.ErrorText, "\r\n|\r|\n").All(l => l.StartsWith("[WARNING]")))
                        {
                            throw new MeshException("Poisson nonempty error output");
                        }
                        
                        if (!File.Exists(outputFile))
                        {
                            throw new MeshException("Poisson no output file");
                        }

                        if (rawReconstructedMeshFile != null)
                        {
                            rawReconstructedMeshFile(outputFile);
                        }

                        result = PLYSerializer.Read(outputFile, readValuesAsNormalLengths: true);
                        
                        if (result.Vertices.Count == 0 || result.Faces.Count == 0)
                        {
                            throw new MeshException("Poisson empty output");
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
                        PreserveErrorInput(pointCloud, inputFile, plyWriter, "cloud", options, logger);
                        Mesh envMesh =
                            options != null && options.Envelope.HasValue ? options.Envelope.Value.ToMesh() : null;
                        PreserveErrorInput(envMesh, envFile, plyWriter, "envelope", options, logger);
                        throw new MeshException("failed to run " + (cfg.PoissonExeLegacy ? "(legacy) " : "") +
                                                reconstructExe + " " + arguments + ": " + ex.Message);
                    }

                    if (options != null && options.Envelope.HasValue && options.ClipToEnvelope)
                    {
                        if (logger != null) logger.LogInfo("clipping mesh to envelope bounds");
                        result.Clip(options.Envelope.Value, normalize: false);
                        if (result.Vertices.Count == 0 || result.Faces.Count == 0)
                        {
                            throw new MeshException("empty output after clipping to envelope");
                        }
                        if (logger != null)
                        {
                            logger.LogInfo("clipped mesh has {0} faces", Fmt.KMG(result.Faces.Count));
                        }
                    }

                    if (options != null && options.MinIslandRatio > 0)
                    {
                        if (logger != null)
                        {
                            logger.LogInfo("removing islands less than {0} times largest island diameter",
                                           options.MinIslandRatio);
                        }
                        nr = result.RemoveIslands(options.MinIslandRatio);
                        if (result.Vertices.Count == 0 || result.Faces.Count == 0)
                        {
                            throw new MeshException("empty output after removing islands");
                        }
                        if (nr > 0 && logger != null)
                        {
                            logger.LogInfo("removed {0} islands, mesh has {1} faces", nr, Fmt.KMG(result.Faces.Count));
                        }
                    }

                    if (untrimmedMeshWithValueScaledNormals != null)
                    {
                        untrimmedMeshWithValueScaledNormals(result);
                    }

                    if (options != null && options.TrimmerLevel > 0)
                    {
                        result = Trim(result, options, logger);
                    }
                });
            });

            return result;
        }

        public static Mesh Trim(Mesh meshWithValueScaledNormals, Options options, ILogger logger = null)
        {
            if (options == null || options.TrimmerLevel <= 0)
            {
                throw new ArgumentException("trimmer level must be > 0, got " +
                                            (options == null ? "null" : options.TrimmerLevel.ToString()));
            }

            var cfg = PoissonConfig.Instance;
            string trimmerExe = Path.Combine(PathHelper.GetApplicationPath(), "ExternalApps", cfg.TrimmerExe);

            var plyWriter = new PLYMaximumCompatibilityWriter(writeNormalLengthsAsValue: true);
            string inputFile = null;

            Mesh result = null;
            TemporaryFile.GetAndDeleteMultiple(2, ".ply", files =>
            {
                inputFile = files[0];
                string outputFile = files[1];
                
                PLYSerializer.Write(meshWithValueScaledNormals, inputFile, plyWriter);
                
                string arguments = string.Format("--in {0} --out {1} --trim {2} {3}",
                                                 inputFile, outputFile, options.TrimmerLevel,
                                                 options.MinIslandRatio > 0 ?
                                                 "--aRatio " + options.MinIslandRatio : "");
                if (logger != null)
                {
                    logger.LogInfo("running command: {0} {1}", trimmerExe, arguments);
                }
                
                var pr = new ProgramRunner(trimmerExe, arguments, captureOutput: true);
                try
                {
                    int exitCode = pr.Run();
                    
                    if (exitCode != 0)
                    {
                        throw new MeshException("trimmer exited with status " + exitCode);
                    }
                    
                    //at least some legacy versions of PoissonRecon.exe can error out but still
                    //have zero exit code and write a valid and nonempty output mesh
                    //it seems the only way to detect that is like this
                    if (cfg.PoissonExeLegacy && !string.IsNullOrEmpty(pr.ErrorText) &&
                        !Regex.Split(pr.ErrorText, "\r\n|\r|\n").All(l => l.StartsWith("[WARNING]")))
                    {
                        throw new MeshException("trimmer nonempty error output");
                    }
                    
                    if (!File.Exists(outputFile))
                    {
                        throw new MeshException("trimmer no output file");
                    }
                }
                catch (Exception ex)
                {
                    if (logger != null)
                    {
                        logger.LogError(pr.OutputText);
                        logger.LogError(pr.ErrorText);
                    }
                    PreserveErrorInput(meshWithValueScaledNormals, inputFile, plyWriter, "trimmer", options, logger);
                    throw new MeshException("failed to run " + (cfg.PoissonExeLegacy ? "(legacy) " : "") +
                                            trimmerExe + " " + arguments + ": " + ex.Message);
                }
                
                result = Mesh.Load(outputFile); //don't scale normals
                
                if (result.Vertices.Count == 0 || result.Faces.Count == 0)
                {
                    throw new MeshException("trimmer empty output");
                }
                if (logger != null)
                {
                    logger.LogInfo("trimmed mesh has {0} faces", Fmt.KMG(result.Faces.Count));
                }
            });
            return result;
        }

        private static void PreserveErrorInput(Mesh mesh, string path, PLYWriter plyWriter, string what,
                                               Options options, ILogger logger)
        {
            try
            {
                if (options == null || !options.PreserveInputsOnError)
                {
                    if (logger != null)
                    {
                        logger.LogWarn("not preserving {0} Poisson input, preservation not enabled", what);
                    }
                    return;
                }
                
                if (mesh == null)
                {
                    if (logger != null)
                    {
                        logger.LogWarn("not preserving {0} Poisson input, not available", what);
                    }
                    return;
                }

                string savePath = what + "_poisson.ply";
                if (options.PreserveInputsOverrideName != null)
                {
                    savePath = options.PreserveInputsOverrideName + "_" + savePath;
                }
                else if (path != null)
                {
                    savePath = StringHelper.GetLastUrlPathSegment(path, stripExtension: true) + "_" + savePath;
                }
                
                if (options.PreserveInputsOverrideFolder != null)
                {
                    string dir = options.PreserveInputsOverrideFolder;
                    savePath = StringHelper.EnsureTrailingSlash(StringHelper.NormalizeSlashes(dir)) + savePath;
                }
                else if (path != null)
                {
                    string dir = StringHelper.StripLastUrlPathSegment(path) ;
                    savePath = StringHelper.EnsureTrailingSlash(StringHelper.NormalizeSlashes(dir)) + savePath;
                }

                if (!File.Exists(savePath))
                {
                    PLYSerializer.Write(mesh, savePath, plyWriter ?? new PLYMaximumCompatibilityWriter());
                    if (logger != null)
                    {
                        logger.LogInfo("preserved Poisson {0} input: {1}", what, savePath);
                    }
                }
                else
                {
                    logger.LogWarn("not preserving Poisson {0} input, file exists: {1}", what, savePath);
                }
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogException(ex, $"error preserving Poisson {what} input: {path}");
                }
            }
        }
    }
}
