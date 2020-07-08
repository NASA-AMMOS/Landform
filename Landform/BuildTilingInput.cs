using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CommandLine;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.Texturing;
using OPS.Pipeline.TilingServer;
using OPS.RayTrace;
using OPS.Util;

/// <summary>
/// Creates leaf, and sometimes also parent, tile meshes and textures for a Landform alignment project.
///
/// This stage typically runs before build-tileset in a Landform contextual or tactical tileset workflow, possibly with
/// blend-images intervening.
///
/// Leaf tile meshes are always created by applying a tiling scheme to subdivide the (finest LOD) scene mesh.  Leaf tile
/// textures are backprojected from observation images in contextual mesh workflows, and (typically) clipped from the
/// source image in tactical tiling workflows.
///
/// For tactical tiling workflows where the input mesh RDR has existing LODs, build-tiling-input will also typically
/// define all parent tile meshes from the coarser LODs of the input mesh.
///
/// The tile meshes and textures are saved to project storage, along with a TileList data product which indexes them
/// and contains some related metadata.  The TileList is referred to by the SceneMesh in the alignment project database
///
/// See build-tileset for more details.
///
/// Example:
///
/// Landform.exe build-tiling-input windjana --meshframe 0311472
///
/// </summary>
namespace OPS.Landform
{
    [Verb("build-tiling-input", HelpText = "builds textured tiles from a full scene mesh")]
    public class BuildTilingInputOptions : TilingCommandOptions
    {
        [Value(0, Required = false, HelpText = "project name, defaults to input mesh basename if --inputmesh and --input texture are specified", Default = null)]
        public override string ProjectName { get; set; }

        [Option(Default = null, HelpText = "Scene mesh texture image to bake into tiles, backproject observations instead if omitted")]
        public string InputTexture { get; set; }

        [Option(Default = false, HelpText = "Replace existing tile mesh texture coordinates with UVAtlas")]
        public bool RedoTileMeshUVs { get; set; }

        [Option(HelpText = "Percentage of pixels to test when deciding to split a tile based on resolution (speed vs quality), 0 disables texture based split", Default = 0.03)]
        public double SplitByTexturePctToTest { get; set; }

        [Option(HelpText = "Percentage of pixels tested that should satisfy the requirement to avoid splitting a tile", Default = 0.5)]
        public double SplitByTexturePctSatisfied { get; set; }

        [Option(HelpText = "Ratio of source pixels to destination pixels that would trigger a split", Default = 16)]
        public double SplitByTextureSamplingRatio { get; set; }

        [Option(HelpText = "Tiling scheme (Bin, QuadX, QuadY, QuadZ, QuadAuto, Oct)", Default = TilingScheme.QuadAuto)]
        public TilingScheme TilingScheme { get; set; }

        [Option(Default = "auto", HelpText = "Texture mode (None, Clip, Bake, Backproject, auto)")]
        public string TextureMode { get; set; }

        [Option(HelpText = "Preferred observation image texture variant (Original, Blurred, Blended), falls back to Original", Default = TextureVariant.Blended)]
        public override TextureVariant TextureVariant { get; set; }

        [Option(HelpText = "Don't inpaint output to fill seams and holes when backprojecting", Default = false)]
        public bool DontInpaint { get; set; }

        [Option(HelpText = "Debug function that skips all tiles except the one with this name", Default = null)]
        public string OnlyTilesNamed { get; set; }

        [Option(HelpText = "Mission to use if creating project (only if --inputmesh and --inputtexture (or texturing disabled)", Default = Mission.None)]
        public Mission Mission { get; set; }

        [Option(HelpText = "Don't use approximated areas for the tilesplit test", Default = false)]
        public bool NoApproxTileSplit { get; set; }
    }

    public class BuildTilingInput : TilingCommand
    {
        public const int DEF_MAX_TILE_RESOLUTION = 256;

        private BuildTilingInputOptions options;

        private TextureMode textureMode = TextureMode.None;

        public BuildTilingInput(BuildTilingInputOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            try
            {
                if (!ParseArgumentsAndLoadCaches())
                {
                    return 0; //help
                }

                bool clipOrBake = textureMode == TextureMode.Clip || textureMode == TextureMode.Bake;

                RunPhase("load input mesh", () => LoadInputMesh(requireUVs: clipOrBake));

                if (clipOrBake)
                {
                    RunPhase("load input image", LoadInputTexture);
                }

                RunPhase("build acceleration datastructures", BuildMeshOperator);

                if (withTextures && textureMode == TextureMode.Backproject)
                {
                    //most of this is needed for texture split criteria in addition to backproject
                    //so needs to be set up before BuildTileTree()
                    RunPhase("checking/generating observation image masks", BuildObservationImageMasks);
                    RunPhase("build observation frustum hulls", BuildObsHulls);
                    RunPhase("build occlusion datastructures", BuildSceneCaster);
                }

                RunPhase("build tile tree", BuildTileTree);

                if (meshLOD.Count > 1)
                {
                    RunPhase("build LOD tile meshes", BuildLODTileMeshes);
                }
                else
                {
                    RunPhase("build leaf meshes", BuildLeafMeshes);
                }

                if (withTextures && textureMode == TextureMode.Backproject)
                {
                    if (options.Colorize)
                    {
                        RunPhase("checking/computing observation image stats", BuildObservationImageStats);
                    }

                    RunPhase("build backproject strategy", InitBackprojectStrategy);
                }

                RunPhase((withTextures ? "build textures and " : "") + "save tiles", BuildTileTexturesAndSaveTiles);
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            StopStopwatch();

            return 0;
        }

        protected bool ParseArgumentsAndLoadCaches()
        {
            if (!base.ParseArgumentsAndLoadCaches(TILING_DIR))
            {
                return false; //help
            }

            if (options.LoadLODs && string.IsNullOrEmpty(options.InputMesh))
            {
                throw new Exception("--loadlods requires --inputmesh");
            }

            if (string.IsNullOrEmpty(options.TextureMode))
            {
                options.TextureMode = "auto";
            }

            if (options.TextureMode.ToLower() == "auto")
            {
                if (!withTextures || tileResolution == 0)
                {
                    textureMode = TextureMode.None;
                }
                else
                {
                    textureMode = DisableDatabase() ? TextureMode.Clip : TextureMode.Backproject;
                }
            }
            else if (!Enum.TryParse<TextureMode>(options.TextureMode, true, out textureMode))
            {
                throw new Exception(string.Format("unknown texture mode \"{0}\"", options.TextureMode));
            }

            if (tileResolution < 0 && textureMode != TextureMode.Clip)
            {
                tileResolution = DEF_MAX_TILE_RESOLUTION;
            }

            pipeline.LogInfo("texture mode: {0}, resolution {1}", textureMode, tileResolution);

            return true;
        }

        private bool DisableDatabase()
        {
            //this is called by hooks from base base.ParseArgumentsAndLoadCaches()
            //so don't use anything that wouldn't be availale yet in that context

            if (string.IsNullOrEmpty(options.InputMesh))
            {
                return false;
            }

            if (options.NoTextures || options.TileResolution == 0)
            {
                return true;
            }

            if (options.TextureMode.ToLower() == "none")
            {
                return true;
            }

            if (options.TextureMode.ToLower() == "backproject")
            {
                return false;
            }

            if (string.IsNullOrEmpty(options.InputTexture))
            {
                return false;
            }

            return true;
        }

        protected override bool AllowUnlimitedTileResolution()
        {
            return true;
        }

        protected override bool RequireSceneMesh()
        {
            return !DisableDatabase();
        }

        protected override Project GetProject()
        {
            if (DisableDatabase())
            {
                string projectName = options.ProjectName;
                if (string.IsNullOrEmpty(projectName))
                {
                    projectName = StringHelper.GetLastUrlPathSegment(options.InputMesh, stripExtension: true);
                    pipeline.LogInfo("inferred project name \"{0}\"", projectName);
                }
                var project = Project.Find(pipeline, projectName);
                if (project != null)
                {
                    return project;
                }
                if (options.Mission == Mission.None)
                {
                    throw new Exception("cannot create project: mission must be specified");
                }
                string productUrl = pipeline.GetStorageUrl(InitializeAlignmentProject.DATA_PRODUCT_DIR, projectName);
                string inputUrl = null;
                bool recreateIfExists = false;
                var init = new InitializeAlignmentProject(pipeline);
                return init.Initialize(projectName, productUrl, inputUrl, options.Mission, recreateIfExists);
            }
            else
            {
                if (string.IsNullOrEmpty(options.ProjectName))
                {
                    throw new Exception("--projectname must be specified");
                }
                return base.GetProject();
            }
        }

        protected override string GetAutoMeshFrame()
        {
            return DisableDatabase() ? "passthrough" : "newest";
        }

        protected override bool PassthroughMeshFrameAllowed()
        {
            return DisableDatabase();
        }

        protected override bool NonPassthroughMeshFrameAllowed()
        {
            return !DisableDatabase();
        }

        protected override void LoadFrameCache()
        {
            if (!DisableDatabase())
            {
                base.LoadFrameCache();
            }
        }

        protected override void LoadObservationCache()
        {
            if (!DisableDatabase())
            {
                base.LoadObservationCache();
            }
        }

        private void LoadInputTexture()
        {
            if (!string.IsNullOrEmpty(options.InputTexture))
            {
                pipeline.LogInfo("loading input texture from {0}", options.InputTexture);
                sceneTexture = pipeline.LoadImage(options.InputTexture);
            }
            else if (project != null && sceneMesh != null)
            {
                Guid texGuid = Guid.Empty;
                switch (options.TextureVariant)
                {
                    case TextureVariant.Original: texGuid = sceneMesh.TextureGuid; break;
                    case TextureVariant.Blurred: texGuid = sceneMesh.BlurredTextureGuid; break;
                    case TextureVariant.Blended: texGuid = sceneMesh.BlendedTextureGuid; break;
                    default: throw new Exception("unhandled texture variant: " + options.TextureVariant);
                }
                if (texGuid != Guid.Empty)
                {
                    pipeline.LogInfo("loading {0} scene texture from database", options.TextureVariant);
                    sceneTexture = pipeline.GetDataProduct<PngDataProduct>(project, texGuid).Image;
                }
                else
                {
                    throw new Exception($"{options.TextureVariant} scene texture in database");
                }
            }
            else
            {
                throw new Exception("cannot load input texture, no scene mesh in database");
            }
        }

        private void BuildTileTree()
        {
            if (meshLOD.Count > 1)
            {
                pipeline.LogInfo("building tile tree from {0} existing LODs, tiling scheme {1}",
                                 meshLOD.Count, options.TilingScheme);
                tileTree = DefineTiles.BuildTileTreeFromLODs(pipeline, options.TilingScheme, meshOpForLOD,
                                                             options.FacesPerTile,
                                                             msg => pipeline.LogInfo(msg),
                                                             msg => pipeline.LogVerbose(msg));
            }
            else
            {
                SplitByTextureOpts texSplitOpts = null;
                if (textureMode == TextureMode.Backproject && options.SplitByTexturePctToTest > 0)
                {
                    CameraInstance toCameraInstance(Observation obs)
                    {
                        var xform = frameCache.GetObservationTransform(obs, meshFrame, options.UsePriors);
                        if (xform == null)
                        {
                            return null;
                        }
                        CameraInstance camInst = new CameraInstance();
                        camInst.cameraToMesh = xform.Mean;
                        camInst.meshToCamera = Matrix.Invert(camInst.cameraToMesh);
                        camInst.cameraModel = obs.CameraModel;
                        camInst.hullInMesh = obsToHull?[obs.Name];
                        camInst.widthPixels = obs.Width;
                        camInst.heightPixels = obs.Height;
                        return camInst;
                    }
                    texSplitOpts = new SplitByTextureOpts()
                    {
                        pctPixelsToTest = options.SplitByTexturePctToTest,
                        pctSampledPixelsSatisfied = options.SplitByTexturePctSatisfied,
                        splitPixelTexelRatio = options.SplitByTextureSamplingRatio,
                        useApproximateTileSplit = !options.NoApproxTileSplit,
                        tileResolution = tileResolution,
                        scInMesh = sceneCaster,
                        cameraInstances = roverImages.Select(obs => toCameraInstance(obs)).ToArray(),
                        raycastTolerance = tcopts.RaycastTolerance
                    };
                }
                double surfaceExtent = sceneMesh != null ? sceneMesh.SurfaceExtent : -1;
                tileTree = DefineTiles.BuildTileTreeFromInputs(pipeline, options.TilingScheme, options.FacesPerTile,
                                                               new List<MeshImagePair>() { new MeshImagePair(mesh) },
                                                               texSplitOpts, surfaceExtent,
                                                               info: msg => pipeline.LogInfo(msg),
                                                               verbose: msg => pipeline.LogVerbose(msg));
            }

            tileTree.DumpStats(msg => pipeline.LogInfo(msg));
        }

        public class NodeLOD : NodeComponent
        {
            public int Lod; //0 = finest
        }

        private void BuildLODTileMeshes()
        {
            if (!string.IsNullOrEmpty(options.OnlyTilesNamed))
            {
                //could be done by seeing if the tile's name starts with the same digits (subtree over named tile)
                throw new NotImplementedException("only for tile not implemented for LODs yet");
            }

            //it is possible that a tiling scheme may choose not to split a node
            //which means there can be leaf nodes at any depth in the tree
            //the approach to map tiling nodes to pre-existing LODs is as follows
            //leaf nodes always clip from the highest LOD mesh
            //parent nodes clip from the next-coarser LOD mesh than the coarsest mesh used by any of their children
            var nodes = new List<SceneNode>();
            int assignLODsAndCollectNodes(SceneNode node)
            {
                int lod = 0;
                if (!node.IsLeaf)
                {
                    foreach (var child in node.Children)
                    {
                        lod = Math.Max(assignLODsAndCollectNodes(child), lod);
                    }
                    lod++;
                }
                node.AddComponent<NodeLOD>().Lod = lod;
                nodes.Add(node); //add parent after children - meshes will be created leaves first and root last
                return lod;
            }

            int rootLOD = assignLODsAndCollectNodes(tileTree);

            pipeline.LogInfo("using {0}/{1} existing LODs", rootLOD + 1, meshLOD.Count);

            int numFailed = 0, curNode = 0, numNodes = nodes.Count, np = 0;
            CoreLimitedParallel.ForEach(nodes, node =>
            {
                Interlocked.Increment(ref curNode);
                Interlocked.Increment(ref np);

                int lod = node.GetComponent<NodeLOD>().Lod;

                pipeline.LogVerbose("building tile mesh {0}/{1} ({2:F2}%){3}: tile {4}, clipping from LOD {5}",
                                    curNode, numNodes, 100 * curNode / (float)numNodes,
                                    np > 1 ? ", processing " + np + " in parallel" : "", node.Name, lod);

                Mesh tileMesh = MakeTileMesh(node, meshOpForLOD[lod]);

                if (tileMesh != null && (!withTextures || tileMesh.HasUVs))
                {
                    node.AddComponent(new MeshImagePair(tileMesh));
                }
                else
                {
                    Interlocked.Increment(ref numFailed);
                }

                Interlocked.Decrement(ref np);
            });

            if (numFailed > 0)
            {
                pipeline.LogWarn("failed to generate meshes for {0} tiles", numFailed);
            }
        }


        private void BuildLeafMeshes()
        {
            int curNode = 0, numFailed = 0, numNodes = tileTree.Leaves().Count(), np = 0;

            string[] onlyTilesNamed = null;
            if (!string.IsNullOrEmpty(options.OnlyTilesNamed))
            {
                onlyTilesNamed = options.OnlyTilesNamed.Split(',');
            }
            CoreLimitedParallel.ForEach(tileTree.Leaves(), leaf =>
            {
                Interlocked.Increment(ref curNode);
                Interlocked.Increment(ref np);

                if (onlyTilesNamed != null && !onlyTilesNamed.Contains(leaf.Name))
                {
                    return;
                }

                pipeline.LogVerbose("building leaf mesh {0}/{1} ({2:F2}%){3}: {4}",
                                    curNode, numNodes, 100 * curNode / (float)numNodes,
                                    np > 1 ? ", processing " + np + " in parallel" : "", leaf.Name);

                Mesh tileMesh = MakeTileMesh(leaf, meshOpForLOD.First());

                if (tileMesh != null && (!withTextures || tileMesh.HasUVs))
                {
                    leaf.AddComponent<MeshImagePair>(new MeshImagePair(tileMesh));
                }
                else
                {
                    Interlocked.Increment(ref numFailed);
                }

                Interlocked.Decrement(ref np);
            });

            if (numFailed > 0)
            {
                pipeline.LogWarn("failed to generate meshes for {0} leaves", numFailed);
            }
        }

        private void BuildTileTexturesAndSaveTiles()
        {
            tileList = new TileList()
            {
                MeshExt = meshExt,
                ImageExt = withTextures ? imageExt : null,
                MeshFrame = meshFrame,
                HasIndexImages = !options.NoIndexImages,
                TilingScheme = options.TilingScheme,
                LeafNames = new List<string>(),
                ParentNames = new List<string>()
            };

            if (sceneMesh != null && sceneMesh.Frame != tileList.MeshFrame)
            {
                throw new Exception(string.Format("existing scene mesh in frame {0} but tile list in frame {1}",
                                                  sceneMesh.Frame, tileList.MeshFrame));
            } 

            var tilesToTexture = tileTree.DepthFirstTraverse()
                .Where(l => l.HasComponent<MeshImagePair>() && l.GetComponent<MeshImagePair>().Mesh != null)
                .ToList();
            int tileCount = tilesToTexture.Count;

            string texMsg = textureMode == TextureMode.Bake ? "baking" :
                textureMode == TextureMode.Backproject ? "backprojecting" :
                textureMode == TextureMode.Clip ? "clipping" :
                "no";

            pipeline.LogInfo("processing {0} tiles, {1} {2}x{2} {3} textures{4}", tileCount, texMsg, tileResolution,
                             options.TextureVariant, options.TextureVariant != TextureVariant.Original ?
                             " (falling back to " + TextureVariant.Original + ")" : "");

            if (textureMode == TextureMode.Backproject)
            {
                pipeline.LogInfo("backproject quality {0}, prefer color {1}, texture far clip {2:f3}",
                                 options.BackprojectQuality, options.PreferColor, options.TextureFarClip);
                if (!options.NoIndexImages)
                {
                    pipeline.LogInfo("saving tile backproject index images");
                }
                pipeline.LogInfo("colorize: {0}", options.Colorize);
            }

            if (meshLOD.Count == 1 && textureMode == TextureMode.Clip)
            {
                //TODO #875
                pipeline.LogWarn("clipping leaf tile textures but baking parent tile textures");
            }

            Image sceneIndex = null;
            if (!options.NoIndexImages)
            {
                pipeline.LogInfo("saving tile backproject index images");

                if (textureMode == TextureMode.Bake || textureMode == TextureMode.Clip)
                {
                    sceneIndex = new Image(3, sceneTexture.Width, sceneTexture.Height);
                    for (int r = 0; r < sceneIndex.Height; r++)
                    {
                        for (int c = 0; c < sceneIndex.Width; c++)
                        {
                            sceneIndex[0, r, c] = 1; //reserve 0 as invalid
                            sceneIndex[1, r, c] = r;
                            sceneIndex[2, r, c] = c;
                        }
                    }
                }
            }

            MultiMeshClipper bakeClipper = null;
            if (textureMode == TextureMode.Bake)
            {
                bakeClipper = new MultiMeshClipper();
                bakeClipper.AddInput(new MeshImagePair(mesh, sceneTexture, sceneIndex));
                bakeClipper.InitTextureBaker();
            }

            int np = 0, curTileNum = 0, numFailed = 0, numSucceded = 0;
            void buildTile(SceneNode tile)
            {
                Interlocked.Increment(ref curTileNum);
                Interlocked.Increment(ref np);

                if (!options.NoProgress)
                {
                    pipeline.LogInfo("{0}saving tile {1}/{2} ({3:F2}%){4}: {5}",
                                     withTextures ? "texturing and " : "", 
                                     curTileNum, tileCount, 100 * curTileNum / (float)tileCount,
                                     np > 1 ? ", processing " + np + " in parallel" : "", tile.Name);
                }

                var mip = tile.GetComponent<MeshImagePair>();

                if (textureMode == TextureMode.Bake)
                {
                    var tmp = bakeClipper.BakeTexture(mip.Mesh, tileResolution, msg => pipeline.LogVerbose(msg));
                    mip.Mesh = tmp.Mesh; //may have been atlassed
                    mip.Image = tmp.Image;
                    mip.Index = tmp.Index;
                }
                else if (textureMode == TextureMode.Backproject)
                {                
                    BackprojectTile(mip, tile.Name, sceneCaster, sceneCaster);
                }
                else if (textureMode == TextureMode.Clip)
                {
                    var texClipper = new TexturedMeshClipper(logger: pipeline, logPrefix: tile.Name);
                    var tmp = texClipper.RemapMeshClipImage(mip.Mesh, sceneTexture, sceneIndex, tileResolution);
                    mip.Mesh = tmp.Mesh; //may have been re-atlassed
                    mip.Image = tmp.Image;
                    mip.Index = tmp.Index;
                }

                if (mip.Mesh != null && (!withTextures || mip.Image != null))
                {
                    SaveTile(mip, tile.Name, localSave, cloudSave, tile.IsLeaf);
                    Interlocked.Increment(ref numSucceded);
                }
                else
                {
                    Interlocked.Increment(ref numFailed);
                }

                //conserve memory
                tile.AddComponent(new MeshImagePairStats(mip));
                tile.RemoveComponent<MeshImagePair>();

                Interlocked.Decrement(ref np);
            }

            //it used to be the case that it was a perf win to build the tiles serially at least when backprojecting
            //but probably not anymore
            //now that PipelineCore implements locking to prevent multiple threads from trying to load the same image
            CoreLimitedParallel.ForEach(tilesToTexture, buildTile);

            if (withTextures && numFailed > 0)
            {
                pipeline.LogWarn("failed to generate textures for {0} tiles", numFailed);
            }

            pipeline.LogInfo("{0} tiles built successfully", numSucceded);

            if (textureMode == TextureMode.Backproject)
            {
                pipeline.LogInfo("backprojected {0} pixels from surface observations, {1} from orbital, {2} failed, " +
                                 "tried up to {3} observations per pixel",
                                 Fmt.KMG(numBackprojectedSurfacePixels), Fmt.KMG(numBackprojectedOrbitalPixels),
                                 Fmt.KMG(numBackprojectFailedPixels), numBackprojectFallbacks + 1);
            }

            tileTree.DumpStats(msg => pipeline.LogInfo(msg));

            if (!options.NoSave)
            {
                if (sceneMesh == null)
                {
                    pipeline.LogInfo("creating scene mesh in frame {0}", tileList.MeshFrame);
                    sceneMesh = SceneMesh.Create(pipeline, project, tileList.MeshFrame);
                }

                pipeline.LogInfo("saving tile list");
                pipeline.SaveDataProduct(project, tileList);
                sceneMesh.TileListGuid = tileList.Guid;
                sceneMesh.Save(pipeline);
            }
        }

        private Mesh MakeTileMesh(SceneNode tile, MeshOperator meshOp)
        {
            Mesh tileMesh = null;

            if (!tile.HasComponent<NodeBounds>())
            {
                throw new Exception(string.Format("tile {0} missing bounds", tile.Name));
            }

            //clip the big mesh to get a tile's mesh
            try
            {
                tileMesh = meshOp.Clip(tile.GetComponent<NodeBounds>().Bounds);
            }
            catch (Exception ex)
            {
                pipeline.LogError("error clipping mesh for tile {0}: {1}", tile.Name, ex.Message);
                return null;
            }

            if (tileMesh.Vertices.Count == 0)
            {
                pipeline.LogWarn("tile {0} empty", tile.Name);
                return null;
            }

            if (textureMode == TextureMode.Bake || textureMode == TextureMode.Backproject)
            {
                if (!tileMesh.HasUVs || options.RedoTileMeshUVs)
                {
                    tileMesh = AtlasMesh(tileMesh, tileResolution, "tile " + tile.Name);
                    if (tileMesh == null)
                    {
                        pipeline.LogError("unknown error atlasing tile mesh {0}", tile.Name);
                        return null;
                    }
                }
                else
                {
                    pipeline.LogVerbose("using existing UVs on tile {0}", tile.Name);
                    tileMesh.RescaleUVs();
                }
            }
            else if (textureMode == TextureMode.Clip && !tileMesh.HasUVs)
            {
                pipeline.LogError("cannot clip texture for tile {0}: scene mesh missing UVs", tile.Name);
                return null;
            }

            return tileMesh;
        }
    }
}
