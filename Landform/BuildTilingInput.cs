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

        [Option(HelpText = "Percentage of pixels to test when deciding to split a tile based on resolution (speed vs quality), 0 disables texture based split", Default = 0.03)]
        public double SplitByTexturePctToTest { get; set; }

        [Option(HelpText = "Percentage of pixels tested that should satisfy the requirement to avoid splitting a tile", Default = 0.5)]
        public double SplitByTexturePctSatisfied { get; set; }

        [Option(HelpText = "Ratio of source pixels to destination pixels that would trigger a split", Default = 16)]
        public double SplitByTextureSamplingRatio { get; set; }

        [Option(HelpText = "Tiling scheme (Bin, QuadX, QuadY, QuadZ, QuadAuto, Oct)", Default = TilingScheme.QuadAuto)]
        public TilingScheme TilingScheme { get; set; }

        [Option(HelpText = "Preferred observation image texture variant (Original, Blurred, Blended), falls back to Original", Default = TextureVariant.Blended)]
        public override TextureVariant TextureVariant { get; set; }

        [Option(HelpText = "Don't inpaint output to fill seams and holes when backprojecting", Default = false)]
        public bool DontInpaint { get; set; }

        [Option(HelpText = "Debug function that skips all tiles except the one with this name", Default = null)]
        public string OnlyTilesNamed { get; set; }

        [Option(HelpText = "Mission to use if creating project (only if --inputmesh and --inputtexture are both specified)", Default = Mission.None)]
        public Mission Mission { get; set; }

        [Option(HelpText = "Don't use approximated areas for the tilesplit test", Default = false)]
        public bool NoApproxTileSplit { get; set; }
    }

    public class BuildTilingInput : TilingCommand
    {
        public const int DEF_MAX_TILE_RESOLUTION = 256;

        private BuildTilingInputOptions options;

        private enum TextureGenMode
        {
            None,       //generate just mesh with no textures
            Clip,       //generate tile textures by clipping regions out of the source texture and offsetting uvs
            Bake,       //generate tile textures by atlassing tiles and sampling source texture at a desired resolution
            Backproject //generate tile textures by choosing the best data from observations that viewed the mesh
        };
        private TextureGenMode texGenMode = TextureGenMode.None;

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

                if (texGenMode == TextureGenMode.Clip || texGenMode == TextureGenMode.Bake)
                {
                    RunPhase("load input image", () => { sceneTexture = pipeline.LoadImage(options.InputTexture); });
                }

                RunPhase("load input mesh", () => LoadInputMesh(requireUVs: texGenMode == TextureGenMode.Clip ||
                                                                texGenMode == TextureGenMode.Bake));

                if (!options.NoSurface && texGenMode == TextureGenMode.Backproject)
                {
                    RunPhase("checking/generating observation image masks", BuildObservationImageMasks);
                    RunPhase("build occlusion datastructures", BuildSceneCaster);
                    RunPhase("build observation frustum hulls", BuildObsHulls);
                }

                RunPhase("build acceleration datastructures", BuildMeshOperator);
                RunPhase("build tile tree", BuildTileTree);

                if (meshLOD.Count > 1)
                {
                    RunPhase("build LOD tile meshes", BuildLODTileMeshes);
                }
                else
                {
                    RunPhase("build leaf meshes", BuildLeafMeshes);
                }

                if (!options.NoSurface && texGenMode == TextureGenMode.Backproject)
                {
                    RunPhase("build backproject strategy", InitBackprojectStrategy);
                }

                if (options.Colorize)
                {
                    RunPhase("checking/computing observation image stats", BuildObservationImageStats);
                }

                RunPhase(string.Format("{0}save tiles", withTextures ? "build tile textures and " : ""),
                         BuildTileTexturesAndSaveTiles);
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

            if (options.LoadLODs && !DisableDatabase())
            {
                throw new Exception("--loadlods requires --inputmesh and --inputtexture");
            }

            if (!withTextures)
            {
                texGenMode = TextureGenMode.None;
            }
            else if (options.LoadLODs)
            {
                //TODO we should probably default to clipping vs baking whenever --inputtexture is given
                //not just when --loadlods is given
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/199
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/713
                //also note: below in BuildTileTexturesAndSaveTiles() we will switch texGenMode to Bake
                //if the input mesh actually only had one LOD (we don't know that yet here)
                texGenMode = TextureGenMode.Clip;
            }
            else if (!string.IsNullOrEmpty(options.InputTexture))
            {
                texGenMode = TextureGenMode.Bake;
            }
            else
            {
                texGenMode = TextureGenMode.Backproject;
            }

            if (tileResolution < 0 && texGenMode != TextureGenMode.Clip)
            {
                tileResolution = DEF_MAX_TILE_RESOLUTION;
            }

            pipeline.LogInfo("texture mode: {0}, resolution {1}", texGenMode, tileResolution);

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

            //if (options.TextureGenMode.ToLower() == "none")
            //{
            //    return true;
            //}

            //if (options.TextureGenMode.ToLower() == "backproject")
            //{
            //    return false;
            //}

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

        private void BuildTileTree()
        {
            if (meshLOD.Count > 1)
            {
                pipeline.LogInfo("building tile tree from {0} existing LODs, tiling scheme {1}",
                                 meshLOD.Count, options.TilingScheme);
                tileTree = DefineTiles.BuildTileTreeFromLODs(pipeline, options.TilingScheme, meshOpForLOD);
            }
            else
            {
                SplitByTextureOpts texSplitOpts = null;
                if (texGenMode == TextureGenMode.Backproject && options.SplitByTexturePctToTest > 0)
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

        private void BuildLODTileMeshes()
        {
            if (!string.IsNullOrEmpty(options.OnlyTilesNamed))
            {
                //could be done by seeing if the tile's name starts with the same digits (subtree over named tile)
                throw new NotImplementedException("only for tile not implemented for LODs yet");
            }

            int numFailed = 0;
            List<SceneNode> curLevelNodes = new List<SceneNode> { tileTree };
            for (int idxTreeLevel = 0; idxTreeLevel < meshLOD.Count; idxTreeLevel++)
            {
                if (!options.NoProgress)
                {
                    pipeline.LogInfo("building LOD tile meshes for tree level {0}/{1} ({2:F2}%)", (idxTreeLevel + 1),
                                     meshLOD.Count, 100 * (idxTreeLevel + 1) / (float)meshLOD.Count);
                }

                // clip meshes for each tile              
                int idxLOD = meshLOD.Count - idxTreeLevel - 1;
                CoreLimitedParallel.ForEach(curLevelNodes, curNode =>
                {
                    Mesh nodeMesh = MakeTileMesh(curNode, meshOpForLOD[idxLOD]);
                    if (nodeMesh != null)
                    {
                        nodeMesh.Clean(); //copying behavior from TextureMeshClipper
                        curNode.AddComponent<MeshImagePair>(new MeshImagePair(nodeMesh));
                    }
                    else
                    {
                        Interlocked.Increment(ref numFailed);
                    }
                });

                // collect next level nodes
                List<SceneNode> nextLevelNodes = new List<SceneNode>();
                foreach (var curNode in curLevelNodes)
                {
                    nextLevelNodes.AddRange(curNode.Children);
                }

                // setup next iteration
                curLevelNodes = nextLevelNodes.Distinct().ToList();
            }

            if (numFailed > 0)
            {
                pipeline.LogWarn("failed to generate meshes for {0} tiles", numFailed);
            }
        }


        private void BuildLeafMeshes()
        {
            int curLeafNum = 0, numFailed = 0, leafCount = tileTree.Leaves().Count();

            string[] onlyTilesNamed = null;
            if (!string.IsNullOrEmpty(options.OnlyTilesNamed))
            {
                onlyTilesNamed = options.OnlyTilesNamed.Split(',');
            }
            CoreLimitedParallel.ForEach(tileTree.Leaves(), leaf =>
            {
                Interlocked.Increment(ref curLeafNum);

                if (onlyTilesNamed != null && !onlyTilesNamed.Contains(leaf.Name))
                {
                    return;
                }

                pipeline.LogVerbose("building leaf mesh {0}/{1} ({2:F2}%): {3}", curLeafNum, leafCount,
                                    100 * curLeafNum / (float)leafCount, leaf.Name);

                Mesh leafMesh = MakeTileMesh(leaf, meshOpForLOD.First());

                if (leafMesh != null)
                {
                    leaf.AddComponent<MeshImagePair>(new MeshImagePair(leafMesh, null));
                    leaf.AddComponent<NodeGeometricError>(new NodeGeometricError(0));
                }
                else
                {
                    Interlocked.Increment(ref numFailed);
                }
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
                HasIndexImages = texGenMode == TextureGenMode.Backproject && !options.NoIndexImages,
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

            string texMsg = texGenMode == TextureGenMode.Bake ? "baking" :
                texGenMode == TextureGenMode.Backproject ? "backprojecting" :
                texGenMode == TextureGenMode.Clip ? "clipping" :
                "no";

            pipeline.LogInfo("processing {0} tiles, {1} {2}x{2} {3} textures{4}", tileCount, texMsg, tileResolution,
                             options.TextureVariant, options.TextureVariant != TextureVariant.Original ?
                             " (falling back to " + TextureVariant.Original + ")" : "");

            if (texGenMode == TextureGenMode.Backproject)
            {
                pipeline.LogInfo("backproject quality {0}, prefer color {1}, texture far clip {2:f3}",
                                 options.BackprojectQuality, options.PreferColor, options.TextureFarClip);
                if (!options.NoIndexImages)
                {
                    pipeline.LogInfo("saving tile backproject index images");
                }
                pipeline.LogInfo("colorize: {0}", options.Colorize);
            }

            if (meshLOD.Count == 1 && texGenMode == TextureGenMode.Clip)
            {
                //TODO for now if the input mesh has only one LOD behave same as if --loadlods was not specified
                texGenMode = TextureGenMode.Bake;
            }

            MultiMeshClipper bakeClipper = null;
            if (texGenMode == TextureGenMode.Bake)
            {
                bakeClipper = new MultiMeshClipper();
                bakeClipper.AddInput(mesh, sceneTexture);
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

                mip.Index = !options.NoIndexImages ? new Image(3, tileResolution, tileResolution) : null;

                if (texGenMode == TextureGenMode.Bake)
                {
                    var tmp = bakeClipper.BakeTexture(mip.Mesh, tileResolution, msg => pipeline.LogVerbose(msg));
                    if (tmp != null)
                    {
                        mip.Mesh = tmp.Mesh;
                        mip.Image = tmp.Image;
                    }
                }
                else if (texGenMode == TextureGenMode.Backproject)
                {                
                    mip.Image = BackprojectTile(tile, mip.Mesh, mip.Index, sceneCaster, sceneCaster);
                }
                else if (texGenMode == TextureGenMode.Clip)
                {
                    var tmp = TexturedMeshClipper.RemapMeshClipImage(mip.Mesh, sceneTexture);
                    //var tmp = TexturedMeshClipper.RemapMeshClipImage(mip.Mesh, sceneTexture, tileResolution);
                    mip.Mesh = tmp.Mesh;
                    mip.Image = tmp.Image;
                }

                if (mip.Mesh != null && (!withTextures || mip.Image != null))
                {
                    SaveTile(tile.Name, mip.Mesh, mip.Image, mip.Index, localSave, cloudSave, tile.IsLeaf);
                    if (options.WriteBackprojectDebug)
                    {
                        SaveImage(Backproject.GenerateIndexPreviewImage(mip.Index), tile.Name + "_index_preview");
                    }
                    Interlocked.Increment(ref numSucceded);
                }
                else
                {
                    Interlocked.Increment(ref numFailed);
                }

                //conserve memory
                tile.AddComponent(new MeshImagePairStats(tile.GetComponent<MeshImagePair>()));
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

            if (texGenMode == TextureGenMode.Backproject)
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

            //assign UVs to the tile vertices iff backproject (not baked) texturing is requested
            if (texGenMode == TextureGenMode.Backproject)
            {
                try
                {
                    tileMesh = UVAtlas.Atlas(tileMesh, tileResolution, tileResolution);
                    if (tileMesh == null)
                    {
                        pipeline.LogError("error atlasing tile mesh {0}: {1}", tile.Name);
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    pipeline.LogError("error atlasing tile mesh {0}: {1}", tile.Name, ex.Message);
                    return null;
                }
            }

            return tileMesh;
        }
    }
}
