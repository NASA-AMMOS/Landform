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
            if (options.ResetDatabase)
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
                        InputId = input.Id,
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



            // Create leaf jobs
            string tileOutputDir = Path.Combine(options.OutputDirectory, "tiles");
            List<List<SceneNode>> leafGroups = new List<List<SceneNode>>();
            GroupSceneNodesIntoJobs(BuildTreeFromNodeRecords(), leafGroups);
            // Create a simulated job queue
            Queue<TilingServerBuildLeafTilesOptions> leafJobs = new Queue<TilingServerBuildLeafTilesOptions>();
            foreach (var group in leafGroups)
            {
                var leafJob = new TilingServerBuildLeafTilesOptions()
                {
                    OutputDirectory = tileOutputDir,
                    TileIds = group.Select(n => n.Name)
                };
                leafJobs.Enqueue(leafJob);
            }

            // Simulate running jobs on a fleet of workers
            foreach (var job in leafJobs)
            {
                var leafer = new TilingServerBuildLeafTiles(job);
                leafer.Run();
            }
            return 0;
        }

        class LeafCountComponent : NodeComponent
        {
            public int NumberOfLeaves;
            public LeafCountComponent() { }
            public LeafCountComponent(int numberOfLeaves) { this.NumberOfLeaves = numberOfLeaves; }
        }

        Queue<SceneNode> GroupSceneNodesIntoJobs(SceneNode node, List<List<SceneNode>> outputGroups, int nodesPerGroup = 32)
        {
            var result = new Queue<SceneNode>();
            if (node.IsLeaf)
            {
                result.Enqueue(node);
                return result;
            }
            foreach (var c in node.Children)
            {
                var tmp = GroupSceneNodesIntoJobs(c, outputGroups, nodesPerGroup);
                foreach(var e in tmp)
                {
                    result.Enqueue(e);
                }
            }
            while(result.Count > nodesPerGroup)
            {
                List<SceneNode> outputGroup = new List<SceneNode>();
                for(int i = 0; i < nodesPerGroup; i++)
                {
                    outputGroup.Add(result.Dequeue());
                }
                outputGroups.Add(outputGroup);
            }
            if(node.Parent == null && result.Count != 0)
            {
                outputGroups.Add(result.ToList());
                result.Clear();
            }
            return result;
        }
        
        public static SceneNode BuildTreeFromNodeRecords()
        {
            var records = PretendTilingServerDatabase.Instance.NodeTable;
            Dictionary<string, SceneNode> idToNode = new Dictionary<string, SceneNode>();
            // Create all nodes
            foreach(var r in records)
            {
                SceneNode node = new SceneNode(r.Id);
                node.AddComponent(new NodeBounds(r.Bounds));
                idToNode.Add(node.Name, node);
            }
            // Connect parents and children
            SceneNode root = null;
            foreach(var r in records)
            {
                SceneNode node = idToNode[r.Id];
                if(r.ParentId == null)
                {
                    root = node;
                }
                else
                {
                    node.Transform.SetParent(idToNode[r.ParentId].Transform);
                }
            }
            return root;
        }

    }
}
