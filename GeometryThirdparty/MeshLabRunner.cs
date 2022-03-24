using log4net;
using JPLOPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#if ENABLE_MESHLAB
namespace OPS.Geometry
{
    /// <summary>
    /// A utility class for running MeshLab
    /// </summary>
    class MeshLabRunner
    {
        /// <summary>
        /// Configure location of meshlab
        /// </summary>
        class MeshLabConfig : Config
        {
            [ConfigEnvironmentVariable("LANDFORM_MESHLAB_DIR")]
            public string MeshLabDir { get; set; }
        }

        private static readonly ILog logger = LogManager.GetLogger(typeof(MeshLabRunner));

        const string DEFAULT_SERVER_DIR = @"C:\Program Files\VCG\MeshLab";
        const string EXECUTABLE_NAME = "meshlabserver.exe";
        static string meshlabServerDir = DEFAULT_SERVER_DIR;

        List<Mesh> inputMeshes = new List<Mesh>();
        string scriptBody;
        MeshLabOutputAttributes attributes;
        string inputExtension;
        string outputExtension;

        /// <summary>
        /// Text output from meshlab
        /// </summary>
        public string OutputText { get; private set; }

        /// <summary>
        /// This variable is used to read or overwrite directory where meshlab is installed
        /// </summary>
        public static string MeshlabServerDirectory
        {
            set
            {
                meshlabServerDir = value;
            }
            get
            {
                return meshlabServerDir;
            }
        }

        static string FullMeshlabServerPath
        {
            get { return Path.Combine(meshlabServerDir, EXECUTABLE_NAME); }
        }


        static MeshLabRunner()
        {
            string meshlabLocation = new MeshLabConfig().MeshLabDir;
            if (meshlabLocation != null)
            {
                logger.Info("Meshlab location specified by config");
                logger.Info(meshlabLocation);
                MeshlabServerDirectory = meshlabLocation;
            }
        }

        /// <summary>
        /// Create a runner
        /// Note that not all filters in mesh lab work correctly with all combinations of input and output file or some attributes
        /// </summary>
        /// <param name="input">input mesh</param>
        /// <param name="scriptBody">content meshlab script to run</param>
        /// <param name="attributes">mesh output attributes</param>
        /// <param name="inputExtension">format of input file to use</param>
        /// <param name="outputExtension">format of output file to use</param>
        public MeshLabRunner(Mesh input, string scriptBody, MeshLabOutputAttributes attributes, string inputExtension, string outputExtension)
        {
            inputMeshes.Add(input);
            this.scriptBody = scriptBody;
            this.attributes = attributes;
            this.inputExtension = inputExtension;
            this.outputExtension = outputExtension;
        }

        /// <summary>
        /// Create a runner
        /// Note that not all filters in mesh lab work correctly with all combinations of input and output file or some attributes
        /// </summary>
        /// <param name="input">list of input meshes</param>
        /// <param name="scriptBody">content meshlab script to run</param>
        /// <param name="attributes">mesh output attributes</param>
        /// <param name="inputExtension">format of input files to use</param>
        /// <param name="outputExtension">format of output file to use</param>
        public MeshLabRunner(List<Mesh> input, string scriptBody, MeshLabOutputAttributes attributes, string inputExtension, string outputExtension)
        {
            inputMeshes.AddRange(input);
            this.scriptBody = scriptBody;
            this.attributes = attributes;
            this.inputExtension = inputExtension;
            this.outputExtension = outputExtension;
        }
        
        /// <summary>
        /// Run Meshlab
        /// </summary>
        /// <returns></returns>
        public Mesh Run()
        {
            if (!File.Exists(FullMeshlabServerPath))
            {
                throw new MeshLabException("Meshlab server not found: " + FullMeshlabServerPath);
            }
            // https://github.com/cnr-isti-vclab/meshlab/issues/164
            if (TemporaryFile.TemporaryDirectory.Contains(" "))
            {
                throw new MeshLabException("TemporaryFile.TemporaryDirectory contains spaces.  A current bug in meshlab prohibits the use of spaces in argument paths.");
            }
            foreach (Mesh m in inputMeshes)
            {
                if (!inputMeshes[0].AttributesEqual(m))
                {
                    throw new MeshLabException("Input meshes to meshlab routines must have matching attributes");
                }
            }
            Mesh result = null;
            TemporaryFile.GetAndDeleteMultiple(inputMeshes.Count, inputExtension, inputFilenames =>
            {
                // Save input meshes as tmp files and add them to the command
                string inputFilenameArguments = "";
                for (int i = 0; i < inputFilenames.Length; i++)
                {
                    inputMeshes[i].Save(inputFilenames[i]);
                    inputFilenameArguments += string.Format(@" -i ""{0}""", inputFilenames[i]);
                }
                bool expectOutputfile = !string.IsNullOrEmpty(outputExtension);                
                // Go ahead and create a temporary file handle for the output even if none is expected
                TemporaryFile.GetAndDelete(expectOutputfile ? outputExtension : "noext", outputFile =>
                {
                    // Output mesh attributes
                    string outputFileArguments = "";
                    if (expectOutputfile)
                    {
                        outputFileArguments = string.Format(@" -o ""{0}"" {1}",  outputFile, attributes.ToString());
                    }
                    // Write out the meshlab script
                    TemporaryFile.GetAndDelete(".mlx", scriptFilename =>
                    {
                        File.WriteAllText(scriptFilename, scriptBody);
                        // Run meshlab
                        string opts = string.Format(@"-s ""{0}"" {1} {2}", scriptFilename, inputFilenameArguments, outputFileArguments);
                        ProgramRunner pr = new ProgramRunner(FullMeshlabServerPath, opts, captureOutput: true, workingDir: Path.GetDirectoryName(inputFilenames[0]));
                        pr.Run();
                        OutputText = pr.OutputText;
                        if (expectOutputfile)
                        {
                            if(!File.Exists(outputFile))
                            {
                                logger.Error(OutputText);
                                logger.Error(pr.ErrorText);
                                throw new MeshLabException("Meshlab failed to produce output file");
                            }                            
                            result = Mesh.Load(outputFile);
                            if (!this.attributes.Matches(result))
                            {
                                throw new MeshLabException("Output mesh did not match desired attributes");
                            }
                        }
                    });

                });
            });
            return result;
        }

        public static void SaveAs(string outputFilename, Mesh mesh, string imagefilename = null)
        {
            var attributes = new MeshLabOutputAttributes(mesh);
            TemporaryFile.GetAndDelete(".ply", f =>
            {
                mesh.Save(f, imagefilename);
                string opts = string.Format("-i \"{0}\" -o \"{1}\" {2}", f, outputFilename, attributes.ToString());
                ProgramRunner pr = new ProgramRunner(FullMeshlabServerPath, opts);
                pr.Run();
            });
        }
    }
}
#endif
