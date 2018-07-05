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

        [Option(Required = false, Default = false, HelpText = "Clears the database")]
        public bool ResetDatabase { get; set; }
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
            PathHelper.EnsureExists(options.OutputDirectory);

            logger.Info("Using pretend database: " + PretendTilingServerDatabase.DatabaseFilename);
            var database = PretendTilingServerDatabase.Instance;
            if(options.ResetDatabase)
            {
                logger.Info("Reseting database");
                database.Clear();
                database.Save();
            }
            if (database.InputTable.Count == 0)
            {
                logger.Info("Adding dataset to database");
                database.InputTable.Add(new TilingInputRecord(options.InputMesh, options.InputTexture));
                database.Save();
            }

            // The chunk input jobs and define nodes job can run in parallel
            Task chunkTask = new Task(() => 
            {
                if (database.ChunkTable.Count == 0)
                {
                    logger.Info("Creating input chunks");
                    // Create a ChunkInput job for each input
                    Queue<TilingServerChunkInputOptions> chunkJobs = new Queue<TilingServerChunkInputOptions>();
                    foreach (var input in database.InputTable)
                    {
                        var job = new TilingServerChunkInputOptions()
                        {
                            OutputDir = options.OutputDirectory,
                            MeshFileapth = input.MeshFilename,
                            ImageFileapth = input.ImageFilename,
                            FacesPerChunk = 250000
                        };
                        chunkJobs.Enqueue(job);
                    }

                    // Simulate cloud workers running chunk jobs
                    foreach (var job in chunkJobs)
                    {
                        var chunker = new TilingServerChunkInput(job);
                        chunker.Run();
                    }
                    database.Save();
                }
            });
            chunkTask.Start();

            Task defineNodesTask = new Task(() =>
            {
                if (database.NodeTable.Count == 0)
                {
                    logger.Info("Creating node definitions");
                    var structureJob = new TilingServerDefineStructureOptions()
                    {
                        TargetFacesPerTile = 2000,
                        MaxResolutionPerTile = 256,
                        TilingScheme = SchemeOption.BIN,
                        SplitAxis = SkirtAxis.None
                    };
                    new TilingServerDefineStructure(structureJob).Run();
                    database.Save();
                }
            });
            defineNodesTask.Start();

            // Wait for both the chunk input and define nodes tasks to complete
            chunkTask.Wait();
            defineNodesTask.Wait();

            return 0;
        }
    }
}
