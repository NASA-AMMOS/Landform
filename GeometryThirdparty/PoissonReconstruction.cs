using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using log4net;
using Microsoft.Xna.Framework;
using OPS.MathExtensions;
using OPS.Util;
using OPS.Imaging;

namespace OPS.Geometry
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

    public class PoissonReconstruction
    {
        public enum BoundaryType { Free = 1, Dirichlet = 2, Neumann = 3 };

        public const PoissonReconstruction.BoundaryType DEF_BOUNDARY_TYPE = PoissonReconstruction.BoundaryType.Neumann;
        public const int DEF_OCTREE_DEPTH = 0;
        public const double DEF_MIN_OCTREE_CELL_WIDTH_METERS = 0.05;
        public const int DEF_MIN_OCTREE_SAMPLES_PER_CELL = 15;
        public const int DEF_BSPLINE_DEGREE = 2;
        public const double DEF_CONFIDENCE_EXP = 0.001;
        public const double DEF_TRIMMER_LEVEL = 9;
        public const double DEF_TRIMMER_LEVEL_LENIENT = 8;
        public const bool DEF_PASS_ENVELOPE_TO_POISSON = false;
        public const bool DEF_CLIP_TO_ENVELOPE = true;
        public const double DEF_MIN_ISLAND_RATIO = 0.2;

        private static readonly ILog logger = LogManager.GetLogger(typeof(PoissonReconstruction));

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

            //exe defaults: 2
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
            opts.ConfidenceExponent = normalsAreScaledByConfidence ? DEF_CONFIDENCE_EXP : 0;

            return Reconstruct(OrganizedPointCloud.BuildPointCloudMesh(points, normals, mask), opts);
        }

        public static Mesh Reconstruct(Mesh pointCloud, Options options = null,
                                       Action<string> rawReconstructedMeshFile = null,
                                       Action<Mesh> untrimmedMeshWithValueScaledNormals = null)
        {
            var cfg = PoissonConfig.Instance;
            string reconstructExe = Path.Combine(PathHelper.GetApplicationPath(), "ExternalApps", cfg.PoissonExe);

            if (pointCloud.Vertices.Count == 0)
            {
                throw new MeshException("Poisson requires non-empty mesh");
            }
            if (!pointCloud.HasNormals)
            {
                throw new MeshException("Poisson requires normals");
            }
            if (pointCloud.ContainsZeroLengthNormals())
            {
                throw new MeshException("Poisson input mesh had zero length normals");
            }
            if (pointCloud.HasUVs)
            {
                throw new MeshException("Poisson meshes cannot have UVs");
            }
            if (pointCloud.HasColors && cfg.PoissonExeLegacy)
            {
                logger.Warn("Poission (legacy) meshes cannot have colors - removing colors");
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
                if (notNormalCount > 0)
                {
                    logger.WarnFormat("Poisson input has {0} non-unit normals, " +
                                      "but not using normals for confidence", notNormalCount);
                }
            }

            Mesh result = null;
            TemporaryFile.GetAndDeleteMultiple(3, ".ply", files =>
            {
                TemporaryFile.GetAndDeleteDirectory(tmpDir =>
                {
                    string inputFile = files[0];
                    string envFile = files[1];
                    string outputFile = files[2];

                    var plyWriter = new PLYMaximumCompatibilityWriter();

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
                        logger.InfoFormat("running command: {0} {1}", reconstructExe, arguments);
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

                        logger.InfoFormat("reconstructed mesh has {0} faces", Fmt.KMG(result.Faces.Count));
                    }
                    catch (Exception ex)
                    {
                        logger.Error(pr.OutputText);
                        logger.Error(pr.ErrorText);
                        throw new MeshException("failed to run " + (cfg.PoissonExeLegacy ? "(legacy) " : "") +
                                                reconstructExe + " " + arguments + ": " + ex.Message);
                    }

                    if (untrimmedMeshWithValueScaledNormals != null)
                    {
                        untrimmedMeshWithValueScaledNormals(result);
                    }

                    if (options != null && options.Envelope.HasValue && options.ClipToEnvelope)
                    {
                        logger.Info("clipping mesh to envelope bounds");
                        result.Clip(options.Envelope.Value, normalize: false);
                        if (result.Vertices.Count == 0 || result.Faces.Count == 0)
                        {
                            throw new MeshException("empty output after clipping to envelope");
                        }
                        logger.InfoFormat("clipped mesh has {0} faces", Fmt.KMG(result.Faces.Count));
                    }

                    if (options != null && options.MinIslandRatio > 0)
                    {
                        logger.InfoFormat("removing islands less than {0} of largest island diameter",
                                          options.MinIslandRatio);
                        result.RemoveIslands(options.MinIslandRatio);
                        if (result.Vertices.Count == 0 || result.Faces.Count == 0)
                        {
                            throw new MeshException("empty output after removing islands");
                        }
                        logger.InfoFormat("island removed mesh has {0} faces", Fmt.KMG(result.Faces.Count));
                    }

                    if (options != null && options.TrimmerLevel > 0)
                    {
                        result = Trim(result, options);
                    }
                });
            });

            return result;
        }

        public static Mesh Trim(Mesh meshWithValueScaledNormals, Options options)
        {
            if (options == null || options.TrimmerLevel <= 0)
            {
                throw new ArgumentException("trimmer level must be > 0, got " +
                                            (options == null ? "null" : options.TrimmerLevel.ToString()));
            }

            var cfg = PoissonConfig.Instance;
            string trimmerExe = Path.Combine(PathHelper.GetApplicationPath(), "ExternalApps", cfg.TrimmerExe);

            Mesh result = null;
            TemporaryFile.GetAndDeleteMultiple(2, ".ply", files =>
            {
                string inputFile = files[0];
                string outputFile = files[1];
                
                var plyWriter = new PLYMaximumCompatibilityWriter(writeNormalLengthsAsValue: true);
                PLYSerializer.Write(meshWithValueScaledNormals, inputFile, plyWriter);
                
                string arguments = string.Format("--in {0} --out {1} --trim {2} {3}",
                                                 inputFile, outputFile, options.TrimmerLevel,
                                                 options.MinIslandRatio > 0 ?
                                                 "--aRatio " + options.MinIslandRatio : "");
                logger.InfoFormat("running command: {0} {1}", trimmerExe, arguments);
                
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
                    logger.Error(pr.OutputText);
                    logger.Error(pr.ErrorText);
                    throw new MeshException("failed to run " + (cfg.PoissonExeLegacy ? "(legacy) " : "") +
                                            trimmerExe + " " + arguments + ": " + ex.Message);
                }
                
                result = Mesh.Load(outputFile); //don't scale normals
                
                if (result.Vertices.Count == 0 || result.Faces.Count == 0)
                {
                    throw new MeshException("trimmer empty output");
                }
                logger.InfoFormat("trimmed mesh has {0} faces", Fmt.KMG(result.Faces.Count));
            });
            return result;
        }
    }
}
