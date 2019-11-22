using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using CommandLine;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.RayTrace;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;
using System.Threading;
using OPS.Pipeline.Texturing;
namespace OPS.Landform
{
    [Verb("build-tiling-input", HelpText = "builds textured tiles from a full scene mesh")]
    public class BuildTilingInputOptions : TilingCommandOptions
    {
        [Value(0, Required = false, HelpText = "project name, defaults to input mesh basename if --inputmesh and --input texture are specified", Default = null)]
        public override string ProjectName { get; set; }

        [Option(Default = null, HelpText = "Scene mesh texture image to bake into tiles, backproject observations instead if omitted")]
        public string InputTexture { get; set; }

        [Option(HelpText = "Percentage of pixels to test when deciding to split a tile based on resolution (speed vs quality), 0 disables texture based split", Default = 0.1)]
        public double SplitByTexturePctToTest { get; set; }

        [Option(HelpText = "Percentage of pixels tested that should satisfy the requirement to avoid splitting a tile", Default = 0.5)]
        public double SplitByTexturePctSatisfied { get; set; }

        [Option(HelpText = "Area of source pixels mapped to a single destination pixel that would trigger a split", Default = 4.5)]
        public double SplitByTextureSamplingRatio { get; set; }

        [Option(HelpText = "Tiling scheme (axis letters indicate the up direction):  Bin, QuadX, QuadY, QuadZ, Oct", Default = TilingScheme.Bin)]
        public TilingScheme TilingScheme { get; set; }

        [Option(HelpText = "Preferred observation image texture variant (Original, Blurred, Blended), falls back to Original", Default = TextureVariant.Blended)]
        public override TextureVariant TextureVariant { get; set; }

        [Option(HelpText = "Don't inpaint output to fill seams and holes when backprojecting", Default = false)]
        public bool DontInpaint { get; set; }

        [Option(HelpText = "Debug function that skips all tiles except the one with this name", Default = null)]
        public string OnlyTilesNamed { get; set; }

        [Option(HelpText = "Mission to use if creating project (only if --inputmesh and --inputtexture are both specified)", Default = Mission.None)]
        public Mission Mission { get; set; }

        [Option(HelpText = "Don't save tile backproject index images", Default = false)]
        public bool NoBackprojectIndexImages { get; set; }

        [Option(HelpText = "save tile backproject index images previews", Default = false)]
        public bool BackprojectIndexImagePreviews { get; set; }

    }

    public class BuildTilingInput : TilingCommand
    {
        private BuildTilingInputOptions options;

        private enum TextureGenMode
        {
            None,           //generate just mesh with no textures
            Clip,           //generate tile textures by clipping regions out of the source texture and offsetting uvs
            Bake,           //generate tile textures by re-atlassing tiles at a desired resolution and resampling source texture
            Backproject     //generate tile textures by choosing the 'best' data from a number of observations that viewed the mesh
        };

        TextureGenMode texGenMode = TextureGenMode.None;

        private Image sceneTexture;
        private SceneNode tileTree;
        private MeshOperator[] meshOps;
        string localTexturingDebugPath;

        public BuildTilingInput(BuildTilingInputOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            StartStopwatch();

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

                RunPhase("load input mesh", () => LoadInputMesh(requireUVs: texGenMode == TextureGenMode.Clip || texGenMode == TextureGenMode.Bake));

                if (texGenMode == TextureGenMode.Backproject)
                {
                    RunPhase("checking/generating observation image masks", BuildObservationImageMasks);
                    RunPhase("build occlusion datastructures", BuildSceneCaster);
                    RunPhase("build observation frustum hulls", BuildObsHulls);
                }

                RunPhase("build tile tree", BuildTileTree);
                RunPhase("build acceleration datastructures", BuildMeshOperator);

                if (texGenMode == TextureGenMode.Backproject)
                {
                    RunPhase(string.Format("initialize observation selection strategy: {0}",options.ObsSelectionStrategy), InitObsSelStrategy);
                }

                if (options.LoadLODs)
                {
                    RunPhase("build LOD tile meshes", BuildLODTileMeshes);
                }
                else
                {
                    RunPhase("build leaf meshes", BuildLeafMeshes);
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

        // runs an exhaustive backproject on a subset of points sampled on the surface of the mesh to refer to later
        private void InitObsSelStrategy()
        {
            //precalculate datastructures for backproject
            var contexts = Backproject.BuildContexts(obsToHull, imageObservations, mission, frameCache,
                                                     observationCache, meshFrame, options.UsePriors, options.OnlyAligned,
                                                     msg => pipeline.LogWarn(msg));

            obsSelStrat.Initialize(mesh, meshOps[0], sceneCaster, contexts, options.TextureResolution, options.BackprojectQuality, options.WriteDebug, localTexturingDebugPath);
        }

        protected override bool ParseArgumentsAndLoadCaches()
        {
            if (!base.ParseArgumentsAndLoadCaches())
            {
                return false; //help
            }

            if (options.LoadLODs && !BakingInputMesh())
            {
                throw new Exception("--loadlods requires --inputmesh and --inputtexture");
            }

            string description = null;
            if (!withTextures)
            {
                texGenMode = TextureGenMode.None;
                description = "not making";
            }
            else if (options.LoadLODs)
            {
                //TODO we should probably default to clipping vs baking whenever --inputtexture is given
                //not just when --loadlods is given
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/199
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/713
                texGenMode = TextureGenMode.Clip;
                description = "clipping from source texture";
                options.NoBackprojectIndexImages = true;
            }
            else if (!string.IsNullOrEmpty(options.InputTexture))
            {
                texGenMode = TextureGenMode.Bake;
                description = "baking from source texture";
                options.NoBackprojectIndexImages = true;
            }
            else
            {
                texGenMode = TextureGenMode.Backproject;
                description = "backprojecting from observations";                
            }

            if (!options.NoBackprojectIndexImages && texGenMode != TextureGenMode.Backproject)
            {
                throw new Exception("not backprojecting textures, cannot save backproject index images");
            }

            pipeline.LogInfo("{0} tile textures", description);

            localTexturingDebugPath = Path.Combine(localOutputPath, "Texturing");
            PathHelper.EnsureExists(localTexturingDebugPath);

            return true;
        }

        private bool BakingInputMesh()
        {
            return !string.IsNullOrEmpty(options.InputMesh) && !string.IsNullOrEmpty(options.InputTexture);
        }

        protected override Project GetProject()
        {
            if (BakingInputMesh())
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
            return BakingInputMesh() ? "passthrough" : "newest";
        }

        protected override bool PassthroughMeshFrameAllowed()
        {
            return BakingInputMesh();
        }

        protected override bool NonPassthroughMeshFrameAllowed()
        {
            return !BakingInputMesh();
        }

        protected override void LoadFrameCache()
        {
            if (!BakingInputMesh())
            {
                base.LoadFrameCache();
            }
        }

        protected override void LoadObservationCache()
        {
            if (!BakingInputMesh())
            {
                base.LoadObservationCache();
            }
        }

        private void BuildTileTree()
        {
            if (options.LoadLODs)
            {
                //use decimated versions of the mesh provided to generate a tile tree with a fixed number of levels
                tileTree = DefineTiles.BuildTileTreeFromLODs(pipeline, options.TilingScheme, meshLODs);
            }
            else
            {
                SplitByTextureOpts texSplitOpts = null;
                if (texGenMode == TextureGenMode.Backproject && options.SplitByTexturePctToTest > 0)
                {
                    texSplitOpts = new SplitByTextureOpts()
                    {
                        pctPixelsToTest = options.SplitByTexturePctToTest,
                        pctSampledPixelsSatisfied = options.SplitByTexturePctSatisfied,
                        subsamplingTriggeringSplit = options.SplitByTextureSamplingRatio,
                        tileResolution = resolution,
                        scInMesh = sceneCaster,
                        cameraInstances =
                        imageObservations
                        .Select(obs => ToCameraInstance((RoverObservation)obs))
                        .ToArray(),
                    };
                }
                tileTree = DefineTiles.BuildTileTreeFromInputs(pipeline, options.TilingScheme, options.FacesPerTile,
                                                               new List<MeshImagePair>() { new MeshImagePair(mesh) },
                                                               texSplitOpts);
            }
        }

        private CameraInstance ToCameraInstance(RoverObservation obs)
        {
            var xform = frameCache.GetObservationTransform(obs, meshFrame, options.UsePriors);
            if (xform == null)
            {
                return null;
            }
            CameraInstance camInst = new CameraInstance();
            camInst.cameraToMesh = xform.Mean;
            camInst.meshToCamera = Matrix.Invert(camInst.cameraToMesh);
            camInst.cameraModel = (CameraModel)JsonHelper.FromJson(obs.CameraModel);
            camInst.hullInMesh = obsToHull[obs.Name];
            camInst.widthPixels = obs.Width;
            camInst.heightPixels = obs.Height;
            return camInst;
        }

        private void BuildMeshOperator()
        {
            if (options.LoadLODs)
            {
                meshOps = new MeshOperator[meshLODs.Count()];
                CoreLimitedParallel.For(0, meshLODs.Count, (idxLOD) =>
                {
                    meshOps[idxLOD] = new MeshOperator(meshLODs.ElementAt(idxLOD), buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false);
                });
            }
            else
            {
                meshOps = new MeshOperator[] { new MeshOperator(mesh, buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false) };
            }
        }

        private void BuildLODTileMeshes()
        {
            if (!string.IsNullOrEmpty(options.OnlyTilesNamed))
            {
                throw new NotImplementedException("only for tile not implemented for LODs yet"); //could be done by seeing if the tile's name starts with the same digits (subtree over named tile)
            }

            int numFailed = 0;
            List<SceneNode> curLevelNodes = new List<SceneNode> { tileTree };
            for (int idxTreeLevel = 0; idxTreeLevel < meshLODs.Count; idxTreeLevel++)
            {
                if (!options.NoProgress)
                {
                    pipeline.LogInfo("building LOD tiles for tree level {0}/{1} ({2:F2}%)", idxTreeLevel, meshLODs.Count,
                                     100 * idxTreeLevel / (float)meshLODs.Count);
                }

                // clip meshes for each tile              
                int idxLOD = meshLODs.Count - idxTreeLevel - 1;
                CoreLimitedParallel.ForEach(curLevelNodes, curNode =>
                {
                    Mesh nodeMesh = MakeTileMesh(curNode, meshOps[idxLOD]);
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

                if (onlyTilesNamed != null && !onlyTilesNamed.Contains<string>(leaf.Name))
                {
                    return;
                }
                else if (!options.NoProgress)
                {
                    pipeline.LogInfo("building leaf mesh {0}/{1} ({2:F2}%): {3}", curLeafNum, leafCount,
                                     100 * curLeafNum / (float)leafCount, leaf.Name);
                }

                Mesh leafMesh = MakeTileMesh(leaf, meshOps.First());

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
                HasIndexImages = !options.NoBackprojectIndexImages,
                TilingScheme = options.TilingScheme,
                LeafNames = new List<string>(),
                ParentNames = new List<string>()
            };

            var tilesToTexture = tileTree.DepthFirstTraverse()
                .Where(l => l.HasComponent<MeshImagePair>() && l.GetComponent<MeshImagePair>().Mesh != null)
                .ToList();
            int tileCount = tilesToTexture.Count;

            MultiMeshClipper bakeClipper = null;
            if (texGenMode == TextureGenMode.Bake)
            {
                bakeClipper = new MultiMeshClipper();
                bakeClipper.AddInput(new MultiMeshClipperInput(mesh, sceneTexture));
                bakeClipper.InitTextureBaker();
            }

            var texMsg = string.Format("{0}x{0} {1} textures{2}",
                                       resolution, options.TextureVariant,
                                       options.TextureVariant != TextureVariant.Original ?
                                       " (falling back to " + TextureVariant.Original + ")" : "");
            pipeline.LogInfo("processing {0} tiles{1}", tileCount,
                             texGenMode == TextureGenMode.Bake ? ", baking " + texMsg :
                             texGenMode == TextureGenMode.Backproject ? ", backprojecting " + texMsg :
                             texGenMode == TextureGenMode.Clip ? ", clipping " + texMsg : "");
            if (texGenMode == TextureGenMode.Backproject && !options.NoBackprojectIndexImages)
            {
                pipeline.LogInfo("saving tile backproject index images");
            }

            int np = 0, curTileNum = 0, numFailed = 0, numSucceded = 0;
            void buildTile(SceneNode tile)
            {
                Interlocked.Increment(ref curTileNum);
                Interlocked.Increment(ref np);

                if (!options.NoProgress)
                {
                    pipeline.LogInfo("{0}saving tile {1}/{2} ({3:F2}%){4}: {5}",
                                     withTextures ? "texturing and " : "", curTileNum, tileCount,
                                     100 * curTileNum / (float)tileCount,
                                     np > 1 ? ", processing " + np + " in parallel" : "", tile.Name);
                }

                MeshImagePair mp = tile.GetComponent<MeshImagePair>();

                Image index = null;
                if (texGenMode == TextureGenMode.Bake)
                {
                    var newMP = bakeClipper.BakeTexture(mp.Mesh, resolution, msg => pipeline.LogInfo(msg));
                    if (newMP != null)
                    {
                        mp.Mesh = newMP.Mesh;
                        mp.Image = newMP.Image;
                    }
                }
                else if (texGenMode == TextureGenMode.Backproject)
                {
                    index = !options.NoBackprojectIndexImages ? new Image(3, resolution, resolution) : null;
                    mp.Image = BackprojectTile(tile, mp.Mesh, index);
                }
                else if (texGenMode == TextureGenMode.Clip)
                {
                    MeshOperator meshOp = null;
                    if (options.LoadLODs)
                    {
                        int idxTreeLevel = tile.Name == "root" ? 0 : tile.Name.Count();
                        int idxLOD = meshLODs.Count() - idxTreeLevel - 1;
                        meshOp = meshOps[idxLOD];
                    }
                    else
                    {
                        meshOp = meshOps.First();
                    }
                    var newMP = TexturedMeshClipper.RemapMeshClipImage(meshOp, mp.Mesh, sceneTexture);
                    mp.Mesh = newMP.Mesh;
                    mp.Image = newMP.Image;
                }

                if (mp.Mesh != null && (!withTextures || mp.Image != null))
                {
                    SaveTile(tile.Name, mp.Mesh, mp.Image, index, localSave, cloudSave, tile.IsLeaf);
                    Interlocked.Increment(ref numSucceded);
                }
                else
                {
                    Interlocked.Increment(ref numFailed);
                }

                tile.RemoveComponent<MeshImagePair>(); //conserve memory

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

            if (!options.NoSave)
            {
                pipeline.LogInfo("saving tile list");
                pipeline.SaveDataProduct(project, tileList);
                sceneMesh.TileListGuid = tileList.Guid;
                sceneMesh.Save(pipeline);
            }
        }

        private void SaveTile(string name, Mesh mesh, Image image, Image index, bool local, bool cloud, bool isLeaf)
        {
            string imgName = image != null ? name + imageExt : null;

            if (local)
            {
                if (image != null)
                {
                    SaveImage(image, name);
                }
                if (index != null)
                {
                    string indexImageName = name + TileList.INDEX_FILE_SUFFIX;
                    SaveFloatTIFF(index, indexImageName);
                    if (options.BackprojectIndexImagePreviews)
                    {
                        Image preview = Backproject.GenerateIndexPreviewImage(index);
                        SaveImage(preview, indexImageName + "_preview");
                    }
                }
                SaveMesh(mesh, name, imgName);
            }

            if (cloud)
            {
                if (image != null)
                {
                    TemporaryFile.GetAndDelete(imageExt, tmpFile =>
                    {
                        image.Save<byte>(tmpFile);
                        string imgUrl = pipeline.GetStorageUrl(outputFolder, project.Name, imgName);
                        pipeline.SaveFile(tmpFile, imgUrl);
                    });
                }

                if (index != null)
                {
                    TemporaryFile.GetAndDelete(".tif", tmpFile =>
                    {
                        var opts = new GDALTIFFWriteOptions(GDALTIFFWriteOptions.CompressionType.DEFLATE);
                        var serializer = new GDALSerializer(opts);
                        serializer.Write<float>(tmpFile, index);
                        string indexName = name + TileList.INDEX_FILE_SUFFIX + TileList.INDEX_FILE_EXT;
                        string indexUrl = pipeline.GetStorageUrl(outputFolder, project.Name, indexName);
                        pipeline.SaveFile(tmpFile, indexUrl);
                    });
                }

                TemporaryFile.GetAndDelete(meshExt, tmpFile =>
                {

                    mesh.Save(tmpFile, imgName);
                    string meshName = name + meshExt;
                    string meshUrl = pipeline.GetStorageUrl(outputFolder, project.Name, meshName);
                    pipeline.SaveFile(tmpFile, meshUrl);

                    if (image != null)
                    {
                        string mtlFile = Path.GetFileNameWithoutExtension(tmpFile) + ".mtl";
                        if (meshExt.ToLower() == ".obj" && File.Exists(mtlFile))
                        {
                            string mtlName = name + ".mtl";
                            string mtlUrl = pipeline.GetStorageUrl(outputFolder, project.Name, mtlName);
                            pipeline.SaveFile(mtlFile, mtlUrl);
                            PathHelper.DeleteWithRetry(mtlFile, pipeline.Logger);
                        }
                    }
                });
            }

            //each tile name is of the form ABCDE... where
            //A is the index of a child of the root
            //B is the index of a child of the node corresponding to A, etc
            //thus each tile name encodes a full path from the root to the tile
            //and the collection of all tile names encodes the full tree topology
            if (isLeaf)
            {
                lock (tileList.LeafNames)
                {
                    tileList.LeafNames.Add(name);
                }
            }
            else
            {               
                lock (tileList.ParentNames)
                {
                    tileList.ParentNames.Add(name);
                }
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
                    tileMesh = UVAtlas.Atlas(tileMesh, options.TextureResolution, options.TextureResolution);
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

        private Image BackprojectTile(SceneNode node, Mesh mesh, Image index = null)
        {
            try
            {
                bool logging = pipeline.Verbose || pipeline.Debug;
                var backprojectResults = BackprojectObservations(mesh, logging, meshName: node.Name, debugOutputPath:localTexturingDebugPath);

                // tile with no textures means it is wholly extrapolation by reconstruction algorithm. skip it.
                if (backprojectResults.Count == 0)
                {
                    return null;
                }

                if (index != null)
                {
                    Backproject.FillIndexImage(backprojectResults, index);
                }

                Image image = new Image(3, resolution, resolution);
                Backproject.FillOutputTexture(pipeline, backprojectResults, image, options.TextureVariant,
                                              !options.DontInpaint, fallbackToOriginal: true);
                return image;
            }
            catch (Exception ex)
            {
                pipeline.LogError("error backprojecting tile {0}: {1}", node.Name, ex.Message);
                return null;
            }
        }
    }
}
