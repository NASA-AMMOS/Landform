using System;
using System.Collections.Generic;
using System.Linq;

using log4net;
using CommandLine;
using Microsoft.Xna.Framework;
using Amazon.DynamoDBv2.DataModel;

using OPS.Cloud;
using OPS.Plumbing;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline.TileServer;
using OPS.Util;

namespace OPS.Pipeline
{
    [Verb("MSL.Texture", HelpText = "generate textures for a terrain mesh")]
    public class TextureMeshOptions
    {
        [Value(0, Required = true, HelpText = "Project name for dynamo db")]
        public string ProjectName { get; set; }

        [Value(1, Required = true, HelpText = "The frame used as the mesh origin")]
        public string MeshOriginFrameName { get; set; }

        [Value(2, Required = true, HelpText = "The dynamo db table prefix")]
        public string DatabaseTablePrefix { get; set; }

    };

    class TextureMesh
    {
        static ILog logger = LogManager.GetLogger(typeof(TextureMesh));
        private TextureMeshOptions options;

        public TextureMesh(TextureMeshOptions opts)
        {
            this.options = opts;
        }

        public int Run()
        {
            logger.Info("Texturing mesh...");

            //QUESTION: static dynamo table prefix?
            PipelineCore pipeline = new PipelineCore(dynamoPrefix: options.DatabaseTablePrefix); //TODO: config.TablePrefix
            pipeline.AddProfile("s3://landlords-dev/", "landlords"); //TODO: not hardcoded

            // build scene graph for the project
            //Frame fullMeshRoot = GetPrimaryMeshFrame(pipeline);
            //Alignment.AlignmentScene scene = BuildSceneGraph(pipeline, fullMeshRoot);

            // download the parent mesh
            TilingInputChunk tilingInput = GetTilingInputChunk(pipeline);
            Mesh fullMesh = DownloadFullMesh(pipeline, tilingInput);

            // collect tiling bounds
            IEnumerable<TilingNode> leafTilingNodes = GetTilingNodes(pipeline);

            // generate leaf tile data
            int tiledMeshes = 0;
            foreach (TilingNode leaf in leafTilingNodes)
            {
                logger.Info("Generating tile " + leaf.Id);
                MeshImagePair leafPair = new MeshImagePair();
                leafPair.Mesh = Mesh.Clip(fullMesh, leaf.GetBounds());

                leafPair.Image = new Image(3, 8, 8);
                leafPair.Image.ApplyInPlace(0, x => { return (byte)255; });

                leaf.SaveMesh(leafPair, pipeline, 0);
                tiledMeshes++;
            }
            logger.Info("Completed generating " + tiledMeshes + " tiles.");
            return 0;
        }
       

        private Frame GetPrimaryMeshFrame(PipelineCore pipeline)
        {
            return Frame.Find(pipeline.DynamoContext, options.ProjectName, options.MeshOriginFrameName);
        }

        private static Alignment.AlignmentScene BuildSceneGraph(PipelineCore pipeline, Cloud.Frame meshRoot)
        {
            logger.Info("Building scene graph for " + meshRoot.Name);

            Alignment.AlignmentScene scene;
            BuildSceneGraph builder = new BuildSceneGraph(pipeline);
            scene = builder.Build(meshRoot, new BuildSceneGraph.Options());

            return scene;
        }

        public TilingInputChunk GetTilingInputChunk(PipelineCore pipeline)
        {
            logger.Info("Get tiling input information");
            return pipeline.DynamoContext.Scan<TilingInputChunk>(new ScanCondition("Id", Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, "FullMesh")).First();
        }
        public Mesh DownloadFullMesh(PipelineCore pipeline, TilingInputChunk input)
        {
            Mesh result = null;
            TemporaryFile.GetAndDelete(".ply", f =>
            {
                logger.Info("Downloading parent mesh: " + input.MeshUrl + input.Id + ".ply");
                pipeline.Storage(input.MeshUrl).DownloadFile(input.MeshUrl + input.Id, f);
                result = Mesh.Load(f);
            });

            return result;
        }

        private IEnumerable<TilingNode> GetTilingNodes(PipelineCore pipeline)
        {
            logger.Info("Collecting tiling information");
            return pipeline.DynamoContext.Scan<TilingNode>(
                new ScanCondition("ProjectName", Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, options.ProjectName),
                new ScanCondition("ChildIds", Amazon.DynamoDBv2.DocumentModel.ScanOperator.IsNull)
            );
        }
    }
}
