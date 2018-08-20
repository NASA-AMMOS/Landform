using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using log4net;
using CommandLine;
using Amazon.DynamoDBv2.DataModel;
using OPS.Plumbing;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline.TileServer;
using OPS.Util;
using OPS.Cloud;

namespace OPS.Pipeline
{
    [Verb("MSL.Texture", HelpText = "generate leaf tiles (mesh and texture) for a terrain mesh")]
    public class TextureMeshOptions
    {
        [Value(0, Required = true, HelpText = "Project name for dynamo db")]
        public string ProjectName { get; set; }
    };

    class TextureMesh
    {
        static ILog logger = LogManager.GetLogger(typeof(TextureMesh));
        private TextureMeshOptions options;

        public TextureMesh(TextureMeshOptions opts)
        {
            this.options = opts;
        }

        /// <summary>
        /// dices a large mesh into the meshes required for the leaf tiling nodes
        /// generates appropriate texture data from observations
        /// uploads data to storage and updates tiling node urls to point at them
        /// </summary>
        /// <returns></returns>
        public int Run()
        {
            logger.Info("Texturing mesh...");

            // setup networking pipe
            PipelineCore pipeline = new PipelineCore(dynamoPrefix:TileServerConfig.Instance.VenueName);
            pipeline.AddProfile(TileServerConfig.Instance.S3Url, TileServerConfig.Instance.Profile);

            // download the parent mesh
            TilingInputChunk tilingInput = GetTilingInputChunk(pipeline);
            Mesh fullMesh = DownloadFullMesh(pipeline, tilingInput);
            fullMesh.Clean();
            MeshOperator op = new MeshOperator(fullMesh);

            // get tiling information
            TilingProject project = TilingProject.Find(pipeline.DynamoContext, this.options.ProjectName);
            SceneNode tilingRoot = TilingNode.BuildTreeFromDatabase(pipeline.DynamoContext, project);

            // generate leaf tile data
            int tiledMeshes = 0;
            int textureDimension = 128;
            int numLeafTileNodes = tilingRoot.Leaves().Count();
            Parallel.ForEach(tilingRoot.Leaves(), leaf =>
            {
                int curTileIndex = Interlocked.Increment(ref tiledMeshes);
                logger.Info("Generating tile number " + curTileIndex + "/" + numLeafTileNodes + " (" + (int)(curTileIndex / (float)numLeafTileNodes * 100) + "%): " + leaf.Name);

                MeshImagePair leafPair = new MeshImagePair();
                leafPair.Mesh = op.Clip(leaf.GetComponent<NodeBounds>().Bounds);

                if(leafPair.Mesh.HasFaces)
                {
                    leafPair.Mesh = UVAtlas.Atlas(leafPair.Mesh, textureDimension, textureDimension, 0, 1, 1);

                    // placeholder solid texture simulating backproject results 
                    leafPair.Image = new Image(3, textureDimension, textureDimension);
                    leafPair.Image.ApplyInPlace(0, x => { return (byte)255; });

                    TilingNode.Find(pipeline.DynamoContext, project, leaf.Name).SaveMesh(leafPair, pipeline, 0);
                }
            });

            logger.Info("Completed generating " + tiledMeshes + " tiles.");
            return 0;
        }

        /// <summary>
        /// gets the tiling input chunk that describes where the large parent mesh
        /// for the project is located
        /// </summary>
        /// <param name="pipeline"></param>
        /// <returns></returns>
        public TilingInputChunk GetTilingInputChunk(PipelineCore pipeline)
        {
            logger.Info("Get tiling input information");

            TilingInputChunk input = pipeline.DynamoContext.Scan<TilingInputChunk>(new ScanCondition("Id", Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, "FullMesh")).First();
            if (input == null)
                throw new CloudException("TilingInputChunk not found");

            return input;
        }

        /// <summary>
        /// downloads the large parent mesh for the project and loads it into memory
        /// </summary>
        /// <param name="pipeline"></param>
        /// <param name="input"></param>
        /// <returns></returns>
        public Mesh DownloadFullMesh(PipelineCore pipeline, TilingInputChunk input)
        {
            Mesh result = null;
            TemporaryFile.GetAndDelete(".ply", f =>
            {
                logger.Info("Downloading parent mesh: " + input.MeshUrl + input.Id + ".ply");
                pipeline.Storage(input.MeshUrl).DownloadFile(input.MeshUrl + input.Id, f);
                result = Mesh.Load(f);
            });

            if (result == null)
                throw new CloudException("Failed to download full project mesh");

            return result;
        }
    }
}
