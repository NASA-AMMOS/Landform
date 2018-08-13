using System;
using log4net;
using CommandLine;
using Microsoft.Xna.Framework;

using OPS.Cloud;
using OPS.Plumbing;
using OPS.Geometry;
using OPS.Imaging;

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
            Frame meshRoot = GetPrimaryMeshFrame(pipeline);
            Alignment.AlignmentScene scene = BuildSceneGraph(pipeline, meshRoot);

            // generate tiled meshes
            //Mesh mesh = DownloadParentMesh(pipeline);
            //BoundingBox[] tileBounds = GetMeshTilingBounds();

            // generate surface textures for all images
            //foreach (BoundingBox bbox in tileBounds)
            //{
            //    MeshDataProduct subMesh = ClipMesh(pipeline, mesh, bbox);
            //    PngDataProduct imageProduct = new PngDataProduct(CreateOutputTexture(subMesh));

            //    //QUESTION: where do these results go?
            //    pipeline.Save(options.ProjectName, imageProduct, false);
            //    pipeline.Save()
            //    //QUESTION: how to enter this into the tiling database
            //}

            // upload textures and meshes
            // update tile server db

            return 0;
        }

        private Image CreateOutputTexture(Mesh subMesh)
        {
            throw new NotImplementedException();
        }

        private Mesh ClipMesh(PipelineCore pipeline, Mesh mesh, BoundingBox bbox)
        {
            throw new NotImplementedException();
        }

        private Frame GetPrimaryMeshFrame(PipelineCore pipeline)
        {
            return Frame.Find(pipeline.DynamoContext, options.ProjectName, options.MeshOriginFrameName);
        }

        private static Alignment.AlignmentScene BuildSceneGraph(PipelineCore pipeline, Cloud.Frame meshRoot)
        {
            Alignment.AlignmentScene scene;
            BuildSceneGraph builder = new BuildSceneGraph(pipeline);
            BuildSceneGraph.Options options = new BuildSceneGraph.Options(); //TODO: build options
            scene = builder.Build(meshRoot, options);
            return scene;
        }

        public Mesh DownloadParentMesh(PipelineCore pipeline)
        {
            //QUESTION: how to get a guid for the master mesh? Store as a value in the dynamo db
            //QUESTION: is there a way to skip going to disk?

            //pipeline.DynamoContext.Find

            //    return context.Load<Mesh>(name, projectName);

            //return pipeline.Get<Mesh>(options.ProjectName, guid, false);
            throw new NotImplementedException();
        }

        private BoundingBox[] GetMeshTilingBounds()
        {
            //QUESTION: how to get tiling information
            //TODO: clip mesh
            //TODO: uvatlas
            throw new NotImplementedException();
        }
    }
}
