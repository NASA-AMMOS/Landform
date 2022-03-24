using log4net;
using JPLOPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JPLOPS.Geometry
{
    public class CloudCompare
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(CloudCompare));

        class CloudCompareConfig : Config
        {
            [ConfigEnvironmentVariable("LANDFORM_CLOUD_COMPARE_EXE")]
            public string CloudCompareExe { get; set; }
        }
        const string DEFAULT_CLOUD_COMPARE_EXE = @"C:\Program Files\CloudCompare\CloudCompare.exe";

        public static string CloudCompareExe { get; set; } = DEFAULT_CLOUD_COMPARE_EXE;

        private delegate string OptionsGenerator(string input, string output);

        static CloudCompare()
        {
            string cloudCompareLocation = new CloudCompareConfig().CloudCompareExe;
            if (cloudCompareLocation != null)
            {
                logger.InfoFormat("CloudCompare location specified by config: {0}", cloudCompareLocation);
                CloudCompareExe = cloudCompareLocation;
            }
        }
        
        static Mesh RunCloudCompare(Mesh input, OptionsGenerator og)
        {
            Mesh r = null;
            TemporaryFile.GetAndDelete(".ply", tmpIn =>
            {
                input.Save(tmpIn);
                TemporaryFile.GetAndDelete(".ply", tmpOut =>
                {
                    RunCloudCompare(tmpOut, og(tmpIn, tmpOut));
                    r = Mesh.Load(tmpOut);
                });
            });
            return r;
        }

        static void RunCloudCompare(string output, string opts)
        {            
            string options = "-SILENT -AUTO_SAVE OFF " + opts;
            ProgramRunner pr = new ProgramRunner(CloudCompareExe, options, captureOutput: true);
            pr.Run();
            if (!File.Exists(output))
            {
                logger.Error(output);
                logger.Error(pr.ErrorText);
                throw new Exception("CloudCompare failed to produce output file");
            }
        }

        public static void GenerateNormalsGridded(string inputFile, string outfile)
        {
            RunCloudCompare(outfile, string.Format("-COMPUTE_NORMALS -O {0} -C_EXPORT_FMT PLY -SAVE_CLOUDS FILE {1}", inputFile, outfile));
        }

        public static Mesh GenerateNormals(Mesh mesh, double radius)
        {
            return RunCloudCompare(mesh, (infile, outfile) =>
            {
                return string.Format("-O  {0} -OCTREE_NORMALS {2} -C_EXPORT_FMT PLY -SAVE_CLOUDS FILE {1}", infile, outfile, radius);
            });
        }
        
        public static Mesh OrientNormals(Mesh mesh, int numberOfNeighbors)
        {
            return RunCloudCompare(mesh, (infile, outfile) =>
            {
                return string.Format("-O  {0} -ORIENT_NORMS_MST {2} -C_EXPORT_FMT PLY -SAVE_CLOUDS FILE {1}", infile, outfile, numberOfNeighbors);
            });
        }

        public static Mesh SORCleaning(Mesh mesh, int numNeighbors, float sig)
        {
            return RunCloudCompare(mesh, (infile, outfile) =>
            {
                return string.Format("-O {0} -SOR {1} {2} -C_EXPORT_FMT PLY -SAVE_CLOUDS FILE {3}", infile, numNeighbors, sig, outfile);
            });
        }

        public static Mesh Subsample(Mesh mesh, float spacing, int oreintNeighbors = 6)
        {
            return RunCloudCompare(mesh, (infile, outfile) =>
            {
                return string.Format("-O {0} -SS SPATIAL {1}  -C_EXPORT_FMT PLY -SAVE_CLOUDS FILE {2}", infile, spacing, outfile);
            });
        }        
    }
}
