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
    public class PoissonConfig : SingletonConfig<PoissonConfig>
    {
        //on some machines the default version of PoissonRecon.exe fails with "Illegal instruction"
        //it is usually possible to workaround this, at least sufficient for dev purposes
        //by setting the environment variable LANDFORM_POISSON_EXE_LEGACY=true

        [ConfigEnvironmentVariable("LANDFORM_POISSON_EXE")]
        public string PoissonExe { get; set; }

        [ConfigEnvironmentVariable("LANDFORM_TRIMMER_EXE")]
        public string TrimmerExe { get; set; }

        [ConfigEnvironmentVariable("LANDFORM_POISSON_EXE_LEGACY")]
        public bool PoissonExeLegacy { get; set; }

        public PoissonConfig()
        {
            if (string.IsNullOrEmpty(PoissonExe)) PoissonExe = "PoissonRecon.V12.0.exe"; //default
            if (PoissonExeLegacy) PoissonExe = "PoissonRecon.exe";

            if (string.IsNullOrEmpty(TrimmerExe)) TrimmerExe = "SurfaceTrimmer.V12.0.exe"; //default
            if (PoissonExeLegacy) TrimmerExe = "SurfaceTrimmer.exe";
        }

        public void Dump(ILog logger)
        {
            logger.Info("Poisson exe: " + PoissonExe + (PoissonExeLegacy ? " (legacy)" : ""));
            logger.Info("SurfaceTrimmer exe: " + TrimmerExe + (PoissonExeLegacy ? " (legacy)" : ""));
        }
    }

    public class PoissonReconstruction
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(PoissonReconstruction));
        public enum BoundaryTypes { Free = 1, Dirichlet = 2, Neumann = 3 };
        private static bool dumpedConfig;

        public class Options
        {
            public BoundaryTypes Boundary;          //exe defaults: Neumann
            public int OctreeDepth;                 //exe defaults: --depth 8, mutually exclusive with MinOctreeCellWidthMeters
            public float MinOctreeCellWidthMeters;  //exe defaults: default doesn't use this parameter, OctreeDepth
            public float MinOctreeSamplesPerCell;   //exe defaults: 1, recommends 1-5 clean data 15-20 noisy data
            public int BSplineDegree;               //exe defaults: 2
            public bool UseNormalsForConfidence;    //exe defaults: no, if true magnitude of normal indicates confidence            
            public double TrimmerLevel;             //exe defaults: 7, if tree level for density is less than amount, remove (higher number == more culling)
            public double TrimmerIslandPct;         //exe defaults: 0.001, if an island surface area is < this percentage of whole mesh surface area, remove it (higher number == more culling)
        };

        /// <summary>
        /// Creates a mesh from a point cloud
        /// </summary>
        /// <returns></returns>
        public static Mesh Reconstruct(Mesh pointCloud, Options options=null)
        {
            if (!dumpedConfig)
            {
                PoissonConfig.Instance.Dump(logger);
                dumpedConfig = true;
            }

            var cfg = PoissonConfig.Instance;
            string reconstructExe = Path.Combine(PathHelper.GetApplicationPath(), "ExternalApps", cfg.PoissonExe);
            string trimmerExe = Path.Combine(PathHelper.GetApplicationPath(), "ExternalApps", cfg.TrimmerExe);

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
            if (pointCloud.HasColors && cfg.PoissonExeLegacy)
            {
                //throw new MeshException("PoissonRecon meshes cannot have colors");
                logger.Warn("PoissionRecon (legacy) meshes cannot have colors - removing colors");
                pointCloud = new Mesh(pointCloud);
                pointCloud.ClearColors();
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

            Mesh result = null;
            Exception ex = null;

            TemporaryFile.GetAndDelete(".ply", inputFile =>
            {
                PLYSerializer.Write(pointCloud, inputFile, new PLYMaximumCompatibilityWriter(false));
                TemporaryFile.GetAndDelete(".ply", reconOutputFile =>
                {
                    TemporaryFile.GetAndDeleteDirectory(tmpDir =>
                    {
                        string arguments = "--in " + inputFile + " --out " + reconOutputFile;

                        if(options.OctreeDepth != 0 && options.MinOctreeCellWidthMeters != 0.0)
                        {
                            throw new MeshException("OctreeDepth and MinOctreeCellWidthMeters are mutually exclusive, but both had requested values");
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

                            arguments += " --normals";              //emit normals from solver
                            arguments += " --tempDir " + tmpDir;
                            
                            if (options != null)
                            {
                                arguments +=
                                    String.Format(" --bType {0} --samplesPerNode {1} --degree {2}{3}{4}{5}{6}",
                                                (int)options.Boundary,
                                                options.MinOctreeSamplesPerCell,
                                                options.BSplineDegree,
                                                options.MinOctreeCellWidthMeters > 0 ? " --width " + options.MinOctreeCellWidthMeters : "",
                                                options.OctreeDepth > 0 ? " --depth " + options.OctreeDepth : "",
                                                options.UseNormalsForConfidence ? " --confidence 2" : "",
                                                options.TrimmerLevel > 0 ? " --density" : "");
                            }

                            //a workaround for running on powerful machines. without it there is an ERROR about not
                            // being able to open a file (likely a bug in multithread buffered file reading)
                            //arguments += " --threads 1";
                            arguments += " --threads " + CoreLimitedParallel.GetMaxDegreeOfParallelism();
                        }

                        logger.Info(String.Format("Running: {0} {1}", reconstructExe, arguments));

                        ProgramRunner pr = new ProgramRunner(reconstructExe, arguments, captureOutput: true);
                        try
                        {
                            int exitCode = pr.Run();

                            if (exitCode != 0)
                            {
                                throw new MeshException("poisson exited with status " + exitCode);
                            }

                            //at least some legacy versions of PoissonRecon.exe can error out but still
                            //have zero exit code and write a valid and nonempty output mesh
                            //it seems the only way to detect that is like this
                            if (cfg.PoissonExeLegacy && !string.IsNullOrEmpty(pr.ErrorText) &&
                                !System.Text.RegularExpressions.Regex.Split(pr.ErrorText, "\r\n|\r|\n") //split lines
                                .All(l => l.StartsWith("[WARNING]")))
                            {
                                throw new MeshException("poisson nonempty error output");
                            }

                            if (!File.Exists(reconOutputFile))
                            {
                                throw new MeshException("poisson no output file");
                            }

                            // if we are using the trimmer we should skip loading it back in
                            //  it will have extra payload per vertex to store density and
                            //  our ply reader may not yet support it
                            if (options == null || options.TrimmerLevel <= 0)
                            {
                                result = Mesh.Load(reconOutputFile);

                                if (result.Vertices.Count == 0 || result.Faces.Count == 0)
                                {
                                    throw new MeshException("empty output");
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            logger.Error(pr.OutputText);
                            logger.Error(pr.ErrorText);
                            ex = new MeshException("Failed to run " + (cfg.PoissonExeLegacy ? "(legacy) " : "") +
                                                   reconstructExe + " " + arguments + ": " + e.Message);
                        }

                        if (options != null && options.TrimmerLevel > 0)
                        {
                            TemporaryFile.GetAndDelete(".ply", trimmerOutputFile =>
                            {
                                arguments = string.Format("--in {0} --out {1} --trim {2} {3}",
                                                          reconOutputFile, trimmerOutputFile,
                                                          (float)options.TrimmerLevel,
                                                          options.TrimmerIslandPct > 0 ?
                                                          "--aRatio " + (float)options.TrimmerIslandPct : "");
                                logger.Info(String.Format("Running: {0} {1}", trimmerExe, arguments));

                                pr = new ProgramRunner(trimmerExe, arguments, captureOutput: true);
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
                                        !System.Text.RegularExpressions.Regex.Split(pr.ErrorText, "\r\n|\r|\n") //split lines
                                        .All(l => l.StartsWith("[WARNING]")))
                                    {
                                        throw new MeshException("trimmer nonempty error output");
                                    }

                                    if (!File.Exists(trimmerOutputFile))
                                    {
                                        throw new MeshException("trimmer no output file");
                                    }

                                    result = Mesh.Load(trimmerOutputFile);

                                    if (result.Vertices.Count == 0 || result.Faces.Count == 0)
                                    {
                                        throw new MeshException("trimmer empty output");
                                    }
                                }
                                catch (Exception e)
                                {
                                    logger.Error(pr.OutputText);
                                    logger.Error(pr.ErrorText);
                                    ex = new MeshException("Failed to run " + (cfg.PoissonExeLegacy ? "(legacy) " : "") +
                                                           trimmerExe + " " + arguments + ": " + e.Message);
                                }
                            });
                        }
                    });
                });
            });

            if (ex != null)
            {
                throw ex;
            }

            return result;
        }

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

            var opts = new Options
            {
                Boundary = PoissonReconstruction.BoundaryTypes.Neumann,
                MinOctreeCellWidthMeters = 0.05f,
                MinOctreeSamplesPerCell = 15,
                OctreeDepth = 0,
                BSplineDegree = 1,
                UseNormalsForConfidence = normalsAreScaledByConfidence
            };

            return Reconstruct(OrganizedPointCloud.BuildPointCloudMesh(points, normals, mask), opts);
        }
    }
}
