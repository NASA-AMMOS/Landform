using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using System.IO;
using OPS.Geometry;
using OPS.Imaging;
using log4net;

namespace OPS.Pipeline
{

    [Verb("tilingserverworkflow", HelpText = "Runs a simulated tiling server workflow locally")]
    public class TilingServerWorkflowOptions
    {
        [Value(0, Required = true, HelpText = "")]
        public string OutputDirectory { get; set; }

        [Value(1, Required = true, HelpText = "")]
        public string InputMesh { get; set; }

        [Value(2, Required = true, HelpText = "")]
        public string InputTexture { get; set; }
    }

    public class TilingServerWorkflow
    {
        static ILog logger = LogManager.GetLogger(typeof(TilingServerWorkflow));


        TilingServerWorkflowOptions options;
        public TilingServerWorkflow(TilingServerWorkflowOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            logger.Info("Using pretend database: " + PretendTilingServerDatabase.DatabaseFilename);
            var database = PretendTilingServerDatabase.Instance;
            database.Clear();
            database.InputTable.Add(new TilingInputRecord(options.InputMesh, options.InputTexture));
            database.Save();

            // Read inputs from "database"
            PathHelper.EnsureExists(options.OutputDirectory);

            // Create a ChunkInput job for each input
            Queue<TilingServerChunkInputOptions> chunkJobs = new Queue<TilingServerChunkInputOptions>();
            foreach(var input in database.InputTable)
            {
                var job = new TilingServerChunkInputOptions();
                job.OutputDir = options.OutputDirectory;
                job.MeshFileapth = input.MeshFilename;
                job.ImageFileapth = input.ImageFilename;
                job.FacesPerChunk = 250000;
                chunkJobs.Enqueue(job);
            }

            // Simulate cloud workers running chunk jobs
            foreach(var job in chunkJobs)
            {
                var chunker = new TilingServerChunkInput(job);
                chunker.Run();
            }
            database.Save();

            return 0;
        }
    }
}
