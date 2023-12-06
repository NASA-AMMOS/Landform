using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.Geometry;

namespace JPLOPS.Pipeline.TilingServer
{
    public class ChunkInputMessage : PipelineMessage
    {
        public string InputName;
        public ChunkInputMessage() { }
        public ChunkInputMessage(string projectName) : base(projectName) { }

        public override string Info()
        {
            return string.Format("[{0}] ChunkInput input {1}", ProjectName, InputName);
        }
    }

    public class ChunkInput : PipelineOperation
    {
        public const string MESH_EXT =  ".ply";
        public const string SPARSE_IMAGE_CHUNK_EXT = ".tif";
        public const int SPARSE_IMAGE_CHUNK_RES = 2048;
        public const int MAX_FACES_PER_CHUNK = TilingDefaults.MAX_FACES_PER_TILE * 10;
        public const double MIN_CHUNK_EXTENT = TilingDefaults.MIN_TILE_EXTENT * 10;

        private readonly ChunkInputMessage message;

        public ChunkInput(PipelineCore pipeline, ChunkInputMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        private class ChunkData
        {
            public string NodeId;
            public BoundingBox Bounds;
            public ChunkData(string id, BoundingBox box)
            {
                NodeId = id;
                Bounds = box;
            }
        }


        public void Process()
        {
            var project = TilingProject.Find(pipeline, projectName);

            var input = TilingInput.Find(pipeline, projectName, message.InputName);
            if (input.Chunked)
            {
                LogLess("input {0} has already been chunked, skipping", message.InputName);
                pipeline.EnqueueToMaster(message);
                return;
            }

            LogLess("downloading and cleaning input mesh {0}", input.MeshUrl);
            Mesh mesh = null;
            pipeline.GetFile(input.MeshUrl, f =>
            {
                mesh = Mesh.Load(f);
                mesh.RemoveInvalidFaces();
                mesh.Clean();
            });
            SparsePipelineImage sparseImage = null;
            string imageBaseUrl = null;
            if (input.ImageUrl != null)
            {
                LogLess("downloading and chunking image {0} from {1}", message.InputName, input.ImageUrl);
                sparseImage = new SparsePipelineImage(pipeline, input.ImageUrl, SPARSE_IMAGE_CHUNK_RES);
                imageBaseUrl = pipeline.GetStorageUrl("chunk", projectName, Guid.NewGuid().ToString());
                LogLess("saving chunks for input {0} to {1}", message.InputName, imageBaseUrl);
                sparseImage.SaveAllChunks<byte>(imageBaseUrl, SPARSE_IMAGE_CHUNK_EXT);
                input.ImageBands = sparseImage.Bands;
                input.ImageWidth = sparseImage.Width;
                input.ImageHeight = sparseImage.Height;
            }

            LogLess("building acceleration structures to chunk input {0}", message.InputName);
            var multiClipper = new MultiMeshClipper(powerOfTwoTextures: project.PowerOfTwoTextures, logger: pipeline);
            multiClipper.AddInput(new MeshImagePair(mesh, sparseImage));

            LogLess("building bounds tree to chunk input {0}", message.InputName);
            var criteria = new TileSplitCriteria[] { new FaceSplitCriteria(MAX_FACES_PER_CHUNK) };
            var root = DefineTiles.BuildBoundsTree(multiClipper, project.TilingScheme, criteria, MIN_CHUNK_EXTENT,
                                                   info: msg => LogLess(msg), verbose: msg => LogVerbose(msg));

            LogLess("building mesh chunks for input {0}", message.InputName);
            ConcurrentBag<string> chunkIds = new ConcurrentBag<string>();
            var leaves = root.Leaves().ToList();
            Serial.ForEach(leaves, (leaf, pls, i) =>
            {
                BoundingBox bounds = leaf.GetComponent<NodeBounds>().Bounds;
                Mesh m = multiClipper.Clip(bounds, ragged: true);
                if (m.Vertices.Count > 0)
                {
                    TemporaryFile.GetAndDelete(MESH_EXT, f =>
                    {
                        m.Save(f);
                        string id = Guid.NewGuid().ToString();
                        string meshUrl = pipeline.GetStorageUrl("chunk", projectName, id + MESH_EXT);
                        pipeline.SaveFile(f, meshUrl);
                        TilingInputChunk.Create(pipeline, id, meshUrl, imageBaseUrl, bounds);
                        chunkIds.Add(id);
                        LogLess("generated chunk {0}/{1} for input {2}", i + 1, leaves.Count, message.InputName);
                    });
                }
            });

            LogLess("saving chunk IDs");
            lock (input.ChunkIds)
            {
                input.ChunkIds.UnionWith(chunkIds);
            }
            input.Chunked = true;
            input.Save(pipeline);
            pipeline.EnqueueToMaster(message);
        }
    }
}
