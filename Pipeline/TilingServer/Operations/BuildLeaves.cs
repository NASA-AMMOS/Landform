using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using Microsoft.Xna.Framework;
using System.Collections.Concurrent;

namespace OPS.Pipeline.TilingServer
{

    public class BuildLeavesMessage : PipelineMessage
    {
        public List<string> TileIds;
        public BuildLeavesMessage() { }
        public BuildLeavesMessage(string projectName) : base(projectName) { }
    }

    public class BuildLeaves : PipelineOperation
    {
        public const int DEF_MAX_TEXTURE_RESOLUTION = 512;

        private readonly BuildLeavesMessage message;

        public BuildLeaves(PipelineCore pipeline, BuildLeavesMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        class InputChunkGroup
        {
            public TilingInput Input;
            public List<TilingInputChunk> Chunks = new List<TilingInputChunk>();
        }

        public void Process()
        {
            LogInfo("starting batch of {0} leaf tiles", message.TileIds.Count);

            var project = TilingProject.Find(pipeline, projectName);

            List<TilingNode> leaves = new List<TilingNode>();
            foreach(var id in message.TileIds)
            {
                leaves.Add(TilingNode.Find(pipeline, projectName, id));
            }

            // Send completion messages for leaves that are already done
            foreach (var n in leaves)
            {
                if (n.MeshUrl != null)
                {
                    LogInfo("leaf {0} already complete, skipping", n.Id);
                    pipeline.EnqueueToMaster(new TileCompletedMessage(projectName) { TileId = n.Id });
                }
            }

            // Filter any completed leaves
            leaves = leaves.Where(n => n.MeshUrl == null).ToList();
            if (leaves.Count == 0)
            {
                LogInfo("all leaves in job already generated");
                return;
            }

            // Get a list of all chunks that overlap with a leaf tile
            LogInfo("collecting input chunks per leaf");
            List<InputChunkGroup> inputGroups = new List<InputChunkGroup>();
            foreach (var inputName in project.LoadInputNames(pipeline))
            {
                var input = TilingInput.Find(pipeline, projectName, inputName);
                if (input == null)
                {
                    throw new Exception("tiling input not found: " + inputName);
                }
                var group = new InputChunkGroup() { Input = input };
                IEnumerable<string> chunks = null;
                lock (input.ChunkIds)
                {
                    chunks = input.ChunkIds.ToArray();
                }
                foreach (var chunkId in chunks)
                {
                    TilingInputChunk chunk = TilingInputChunk.Find(pipeline, chunkId);
                    bool anyIntersect = leaves.Any(leaf => leaf.GetBoundsChecked().Intersects(chunk.GetBounds()));
                    if (anyIntersect)
                    {
                        group.Chunks.Add(chunk);
                    }
                }
                if (group.Chunks.Count > 0)
                {
                    inputGroups.Add(group);
                }
            }

            bool needUVs = project.TextureMode == TextureMode.Bake || project.TextureMode == TextureMode.Clip;

            TextureProjector textureProjector = null;
            if (project.TextureProjectorGuid != Guid.Empty && needUVs)
            {
                textureProjector = pipeline.GetDataProduct<TextureProjector>(project, project.TextureProjectorGuid);
            }

            // Reconstruct a mesh for each input using only the chunks that overlap with leaves that we are building
            LogInfo("building acceleration datastructures");
            bool hasImages = false;
            bool hasUVs = false;
            var clipper = new MultiMeshClipper(powerOfTwoTextures: project.PowerOfTwoTextures, logger: pipeline);
            foreach (var group in inputGroups)
            {
                var meshes = group.Chunks.Select(c => Mesh.Load(pipeline.GetFileCached(c.MeshUrl, "meshes"))).ToArray();
                var mergedMesh = Mesh.Merge(meshes);
                mergedMesh.Clean();
                SparsePipelineImage image = null;
                string chunkBaseUrl = group.Chunks[0].ImageUrl;
                if (chunkBaseUrl != null)
                {
                    hasImages = true;
                    TilingInput ti = group.Input;
                    image = new SparsePipelineImage(pipeline, ti.ImageBands, ti.ImageWidth, ti.ImageHeight,
                                                    chunkBaseUrl, ChunkInput.IMAGE_EXT, ChunkInput.CHUNK_RESOLUTION);
                }
                if (!mergedMesh.HasUVs && needUVs && textureProjector != null)
                {
                    LogInfo("atlasing input mesh with texture projection");
                    mergedMesh.ProjectTexture(textureProjector.ImageWidth, textureProjector.ImageHeight,
                                              textureProjector.CameraModel, meshToImage: textureProjector.MeshToImage);
                }
                hasUVs = hasUVs || mergedMesh.HasUVs;
                clipper.AddInput(mergedMesh, image);
            }

            int maxTexRes = project.MaxTextureResolution;

            if (needUVs && !hasUVs)
            {
                LogWarn("cannot {0} leaf textures: input mesh(es) missing UVs", project.TextureMode);
                maxTexRes = 0;
            }

            if (hasImages && hasUVs && maxTexRes != 0)
            {
                switch (project.TextureMode)
                {
                    case TextureMode.None: maxTexRes = 0; break;
                    case TextureMode.Bake: 
                    {
                        if (maxTexRes < 0)
                        {
                            maxTexRes = TilingDefaults.MAX_TEXTURE_RESOLUTION;
                        }
                        clipper.InitTextureBaker();
                        break;
                    }
                    case TextureMode.Clip: break;
                    case TextureMode.Backproject:
                    {
                        LogWarn("unsupported texture mode, not generating leaf textures: {0}", project.TextureMode);
                        maxTexRes = 0;
                        break;
                    }
                }
            }

            LogInfo("building {0} leaves", leaves.Count);
            int nc = inputGroups.SelectMany(g => g.Chunks).Count();
            int nl = 0;
            CoreLimitedParallel.ForEach(leaves, leaf =>
            {              
                Interlocked.Increment(ref nl);
                LogInfo("building leaf {0} from {1} chunks ({2}/{3})", leaf.Id, nc, nl, leaves.Count);

                BoundingBox bounds = leaf.GetBoundsChecked();
                MeshImagePair pair = null;
                if (hasImages && hasUVs && maxTexRes != 0)
                {
                    if (project.TextureMode == TextureMode.Bake)
                    {
                        var mesh = clipper.Clip(bounds);
                        LogInfo("baking {0}x{0} leaf texture, {1}", maxTexRes, maxTexRes,
                                mesh.HasUVs ? "using exising UVs" : "assigning new UVs with UVAtlas");
                        if (mesh.HasUVs)
                        {
                            mesh.RescaleUVsForTexture(maxTexRes, maxTexRes, project.MaxTextureStretch);
                        }
                        //BakeTexture() will call UVAtlas if necessary
                        pair = clipper.BakeTexture(mesh, maxTexRes, project.MaxTextureStretch, msg => LogInfo(msg));
                    }
                    else if (project.TextureMode == TextureMode.Clip)
                    {
                        LogInfo("clipping leaf texture");
                        pair = clipper.ClipWithTexture(bounds, maxTexRes);
                    }
                    if (pair.Mesh != null && pair.Image != null &&
                        project.MaxTextureStretch < 1 && !project.PowerOfTwoTextures)
                    {
                        pair.Image = pair.Mesh.ClipImageAndRemapUVs(pair.Image);
                    }
                }
                else
                {
                    pair = new MeshImagePair(clipper.Clip(bounds), null);
                }

                if (pair != null && pair.Mesh != null)
                {
                    var img = pair.Image;
                    LogInfo("saving leaf tile mesh with {0} triangles{1}", Fmt.KMG(pair.Mesh.Faces.Count),
                            img != null ? string.Format(" and {0}x{1} image", img.Width, img.Height) : " (no image)");
                    leaf.SaveMesh(pair, pipeline, project);
                    leaf.Save(pipeline);
                }
                else
                {
                    LogError("failed to build leaf");
                }

                pipeline.EnqueueToMaster(new TileCompletedMessage(projectName) { TileId = leaf.Id });
            });

            LogInfo("batch completed, generated {0} leaf tiles", nl);
        }
    }
}
