using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using JPLOPS.Util;
using JPLOPS.Geometry;
using Microsoft.Xna.Framework;

namespace JPLOPS.Pipeline.TilingServer
{

    public class BuildLeavesMessage : PipelineMessage
    {
        public List<string> TileIds;
        public BuildLeavesMessage() { }
        public BuildLeavesMessage(string projectName) : base(projectName) { }

        public override string Info()
        {
            return string.Format("[{0}] BuildLeaves tiles {1}", ProjectName, string.Join(",", TileIds));
        }
    }

    public class BuildLeaves : PipelineOperation
    {
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
            LogLess("starting batch of {0} leaf tiles", message.TileIds.Count);

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
                    LogLess("leaf {0} already complete, skipping", n.Id);
                    pipeline.EnqueueToMaster(new TileCompletedMessage(projectName) { TileId = n.Id });
                }
            }

            // Filter any completed leaves
            leaves = leaves.Where(n => n.MeshUrl == null).ToList();
            if (leaves.Count == 0)
            {
                LogLess("all leaves in job already generated");
                return;
            }

            // Get a list of all chunks that overlap with a leaf tile
            LogLess("collecting input chunks per leaf");
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

            bool inputNeedsUVs = project.TextureMode == TextureMode.Bake || project.TextureMode == TextureMode.Clip;

            TextureProjector textureProjector = null;
            if (project.TextureProjectorGuid != Guid.Empty && inputNeedsUVs)
            {
                textureProjector =
                    pipeline.GetDataProduct<TextureProjector>(project, project.TextureProjectorGuid, noCache: true);
            }

            // Reconstruct a mesh for each input using only the chunks that overlap with leaves that we are building
            LogLess("building acceleration datastructures");
            bool inputHasImages = false;
            bool inputHasUVs = true;
            var clipper = new MultiMeshClipper(powerOfTwoTextures: project.PowerOfTwoTextures, logger: pipeline);
            foreach (var group in inputGroups)
            {
                var meshes = group.Chunks.Select(c => Mesh.Load(pipeline.GetFileCached(c.MeshUrl, "meshes"))).ToArray();
                var mergedMesh = MeshMerge.Merge(meshes);
                mergedMesh.Clean();
                SparsePipelineImage image = null;
                string chunkBaseUrl = group.Chunks[0].ImageUrl;
                if (chunkBaseUrl != null)
                {
                    inputHasImages = true;
                    TilingInput ti = group.Input;
                    image = new SparsePipelineImage(pipeline, ti.ImageBands, ti.ImageWidth, ti.ImageHeight,
                                                    chunkBaseUrl, ChunkInput.SPARSE_IMAGE_CHUNK_EXT,
                                                    ChunkInput.SPARSE_IMAGE_CHUNK_RES);
                }
                if (!mergedMesh.HasUVs && inputNeedsUVs && textureProjector != null)
                {
                    LogInfo("atlasing input mesh with texture projection");
                    mergedMesh.ProjectTexture(textureProjector.ImageWidth, textureProjector.ImageHeight,
                                              textureProjector.CameraModel, meshToImage: textureProjector.MeshToImage);
                }
                inputHasUVs &= mergedMesh.HasUVs;
                clipper.AddInput(new MeshImagePair(mergedMesh, image));
            }

            int maxTexRes = project.MaxTextureResolution;

            if (inputNeedsUVs && !inputHasUVs)
            {
                LogWarn("cannot {0} leaf textures: input mesh(es) missing UVs", project.TextureMode);
                maxTexRes = 0;
            }

            if (inputHasImages && inputHasUVs && maxTexRes != 0)
            {
                switch (project.TextureMode)
                {
                    case TextureMode.None: maxTexRes = 0; break;
                    case TextureMode.Bake: 
                    {
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

            BoundingBox? surfaceBounds = project.GetSurfaceBoundingBox();

            LogLess("building {0} leaves", leaves.Count);
            int nc = inputGroups.SelectMany(g => g.Chunks).Count();
            int nl = 0;
            CoreLimitedParallel.ForEach(leaves, leaf =>
            {              
                Interlocked.Increment(ref nl);
                LogLess("building leaf {0} from {1} chunks ({2}/{3})", leaf.Id, nc, nl, leaves.Count);

                BoundingBox bounds = leaf.GetBoundsChecked(); //these bounds may just partition space
                MeshImagePair pair = null;
                if (inputHasImages && inputHasUVs && maxTexRes != 0)
                {
                    int tileResolution = maxTexRes;
                    Mesh mesh = null;
                    if (project.TextureMode == TextureMode.Bake || project.TextureMode == TextureMode.Clip)
                    {
                        mesh = clipper.Clip(bounds);
                        double texelsPerMeter = project.GetMaxTexelsPerMeter(bounds, surfaceBounds);
                        tileResolution = SceneNodeTilingExtensions
                            .GetTileResolution(mesh, maxTexRes, -1, texelsPerMeter, project.PowerOfTwoTextures);
                    }

                    if (project.TextureMode == TextureMode.Bake)
                    {
                        LogLess("baking {0}x{0} leaf texture, {1}", tileResolution, tileResolution,
                                mesh.HasUVs ? "using exising UVs" : "assigning new UVs with UVAtlas");
                        if (mesh.HasUVs)
                        {
                            mesh.RescaleUVsForTexture(tileResolution, tileResolution, project.MaxTextureStretch);
                        }
                        //BakeTexture() will call UVAtlas if necessary
                        pair =
                            clipper.BakeTexture(mesh, tileResolution, project.MaxTextureStretch, msg => LogLess(msg));
                    }
                    else if (project.TextureMode == TextureMode.Clip)
                    {
                        LogLess("clipping leaf texture");
                        pair = clipper.ClipWithTexture(bounds, tileResolution, project.MaxTexelsPerMeter);
                    }
                    if (pair.Mesh != null && pair.Image != null &&
                        project.MaxTextureStretch < 1 && !project.PowerOfTwoTextures)
                    {
                        pair.Image = pair.Mesh.ClipImageAndRemapUVs(pair.Image, ref pair.Index);
                    }
                }
                else
                {
                    pair = new MeshImagePair(clipper.Clip(bounds), null);
                }

                if (pair != null && pair.Mesh != null)
                {
                    var img = pair.Image;
                    LogLess("saving leaf tile mesh with {0} triangles{1}", Fmt.KMG(pair.Mesh.Faces.Count),
                            img != null ? string.Format(" and {0}x{1} image", img.Width, img.Height) : " (no image)");
                    leaf.SetBounds(pair.Mesh.Bounds()); //reset bounds tight to actual leaf geometry
                    leaf.SaveMesh(pair, pipeline, project);
                    leaf.Save(pipeline);
                }
                else
                {
                    throw new Exception("failed to build leaf " + leaf.Id);
                }

                pipeline.EnqueueToMaster(new TileCompletedMessage(projectName) { TileId = leaf.Id });
            });

            LogLess("batch completed, generated {0} leaf tiles", nl);
        }
    }
}
