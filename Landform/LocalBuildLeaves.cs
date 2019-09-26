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

namespace OPS.Landform
{
    [Verb("local-build-leaves", HelpText = "builds textured leaf tiles from a full scene mesh")]
    public class LocalBuildLeavesOptions : TilingCommandOptions
    {
        [Value(0, Required = false, HelpText = "project name, defaults to input mesh basename if --inputmesh and --input texture are specified", Default = null)]
        public override string ProjectName { get; set; }

        [Value(1, Required = false, Default = null, HelpText = "Scene mesh texture image to bake into leaves, backproject observations instead if omitted")]
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
        public string OnlyTileNamed { get; set; }

        [Option(HelpText = "Mission to use if creating project (only if --inputmesh and --inputtexture are both specified)", Default = Mission.None)]
        public Mission Mission { get; set; }
    }

    public class LocalBuildLeaves : TilingCommand
    {
        private LocalBuildLeavesOptions options;

        private bool bakeTextures;
        private bool backprojectTextures;

        private Image sceneTexture;
        private IDictionary<string, ConvexHull> obsToHull;
        private SceneNode tileTree;
        private MeshOperator meshOp;

        public LocalBuildLeaves(LocalBuildLeavesOptions options) : base(options)
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

                if (bakeTextures)
                {
                    RunPhase("load input image", () => { sceneTexture = pipeline.LoadImage(options.InputTexture); });
                }

                RunPhase("load input mesh", () => LoadInputMesh(requireUVs: bakeTextures));

                if (backprojectTextures)
                {
                    RunPhase("checking/generating observation image masks", BuildObservationImageMasks);
                    RunPhase("build occlusion datastructures", BuildSceneCaster);
                    RunPhase("build observation frustum hulls", BuildObsHulls);
                }

                RunPhase("build tile tree", BuildTileTree);
                RunPhase("build acceleration datastructures", BuildMeshOperator);
                RunPhase("build leaf meshes", BuildLeafMeshes);

                RunPhase(string.Format("{0}save leaves", withTextures ? "build leaf textures and " : ""),
                         BuildLeafTexturesAndSaveLeaves);
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            StopStopwatch();

            return 0;
        }

        protected override bool ParseArgumentsAndLoadCaches()
        {
            if (!base.ParseArgumentsAndLoadCaches())
            {
                return false; //help
            }
            bakeTextures = withTextures && !string.IsNullOrEmpty(options.InputTexture);
            backprojectTextures = withTextures && !bakeTextures;
            pipeline.LogInfo("{0} leaf textures",
                             withTextures ? (bakeTextures ? "baking" : "backprojecting") : "not making");
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

        protected override void LoadObservationCache(ObservationType[] obsTypes, bool onlyObsForReconstruction)
        {
            if (!BakingInputMesh())
            {
                base.LoadObservationCache(obsTypes, onlyObsForReconstruction);
            }
        }

        private void BuildObsHulls()
        {
            obsToHull = Backproject.BuildConvexHulls(pipeline, frameCache, meshFrame, options.UsePriors,
                                                     options.OnlyAligned, imageObservations);
        }

        private void BuildTileTree()
        {
            SplitByTextureOpts texSplitOpts = null;
            if (backprojectTextures && options.SplitByTexturePctToTest > 0)
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
            meshOp = new MeshOperator(mesh, buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false);
        }

        private void BuildLeafMeshes()
        {
            int curLeafNum = 0, numFailed = 0, leafCount = tileTree.Leaves().Count();
            CoreLimitedParallel.ForEach(tileTree.Leaves(), leaf =>
            {
                Interlocked.Increment(ref curLeafNum);

                if (!string.IsNullOrEmpty(options.OnlyTileNamed) && options.OnlyTileNamed != leaf.Name)
                {
                    return;
                }
                else if (!options.NoProgress)
                {
                    pipeline.LogInfo("building leaf mesh {0}/{1} ({2}%): {3}", curLeafNum, leafCount,
                                     (int)(100 * curLeafNum / (float)leafCount), leaf.Name);
                }

                Mesh leafMesh = MakeLeafMesh(leaf, meshOp);

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

        private void BuildLeafTexturesAndSaveLeaves()
        {
            leafList = new LeafList()
                {
                    MeshExt = meshExt,
                    ImageExt = withTextures ? imageExt : null,
                    MeshFrame = meshFrame,
                    TilingScheme = options.TilingScheme,
                    LeafNames = new List<string>()
                };
            
            var leavesToTexture = tileTree.Leaves()
                .Where(l => l.HasComponent<MeshImagePair>() && l.GetComponent<MeshImagePair>().Mesh != null)
                .ToList();
            int leafCount = leavesToTexture.Count;

            MultiMeshClipper bakeClipper = null;
            if (backprojectTextures)
            {
                bakeClipper = new MultiMeshClipper();
                bakeClipper.AddInput(new MultiMeshClipperInput(mesh, sceneTexture));
                bakeClipper.InitTextureBaker();
            }

            var texMsg = string.Format("{0}x{0} {1} textures (falling back to {2})",
                                       resolution, options.TextureVariant, TextureVariant.Original);
            pipeline.LogInfo("processing {0} leaves{1}", leafCount, 
                             bakeTextures ? ", baking " + texMsg :
                             backprojectTextures ? ", backprojecting " + texMsg : "");

            //it's actually a significant win to build the leaves serially when backprojecting
            int np = 0, curLeafNum = 0, numFailed = 0, numSucceded = 0;
            void buildLeaf(SceneNode leaf)
            {
                Interlocked.Increment(ref curLeafNum);
                Interlocked.Increment(ref np);

                if (!options.NoProgress)
                {
                    pipeline.LogInfo("{0}saving leaf {1}/{2} ({3}%){4}: {5}",
                                     withTextures ? "texturing and " : "", curLeafNum, leafCount,
                                     (int)(100 * curLeafNum / (float)leafCount),
                                     np > 1 ? ", processing " + np + " in parallel" : "", leaf.Name);
                }

                MeshImagePair mp = leaf.GetComponent<MeshImagePair>();

                if (bakeTextures)
                {
                    var newMP = bakeClipper.BakeTexture(mp.Mesh, resolution, msg => pipeline.LogInfo(msg));
                    mp.Mesh = newMP.Mesh;
                    mp.Image = newMP.Image;
                }
                else if (backprojectTextures)
                {
                    mp.Image = BackprojectLeafTexture(leaf, mp.Mesh);
                }

                if (!withTextures || mp.Image != null)
                {
                    SaveLeaf(leaf.Name, mp.Mesh, mp.Image, localSave, cloudSave);
                    Interlocked.Increment(ref numSucceded);
                }
                else
                {
                    Interlocked.Increment(ref numFailed);
                }

                leaf.RemoveComponent<MeshImagePair>(); //conserve memory

                Interlocked.Decrement(ref np);
            }

            //it used to be the case that it was a perf win to build the leaves serially at least when backprojecting
            //but probably not anymore
            //now that PipelineCore implements locking to prevent multiple threads from trying to load the same image
            //Serial.ForEach(leavesToTexture, buildLeaf);
            CoreLimitedParallel.ForEach(leavesToTexture, buildLeaf);

            if (withTextures && numFailed > 0)
            {
                pipeline.LogWarn("failed to generate textures for {0} leaves", numFailed);
            }

            pipeline.LogInfo("{0} leaf tiles built successfully", numSucceded);

            if (!options.NoSave)
            {
                pipeline.LogInfo("saving leaf list");
                pipeline.SaveDataProduct(project, leafList);
                sceneMesh.LeafListGuid = leafList.Guid;
                sceneMesh.Save(pipeline);
            }
        }

        private void SaveLeaf(string name, Mesh mesh, Image image, bool local, bool cloud)
        {
            string imgName = image != null ? name + imageExt : null;
            
            if (local)
            {
                if (image != null)
                {
                    SaveImage(image, name);
                }
                SaveMesh(mesh, name, imgName);
            }

            if (cloud)
            {
                if (image != null)
                {
                    TemporaryFile.GetAndDelete(imageExt, tmpFile => {
                            image.Save<byte>(tmpFile);
                            string imgUrl = pipeline.GetStorageUrl(outputFolder, project.Name, imgName);
                            pipeline.SaveFile(tmpFile, imgUrl);
                        });
                }
                
                TemporaryFile.GetAndDelete(meshExt, tmpFile => {
                        
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

            lock (leafList.LeafNames)
            {
                //each leaf name is of the form ABCDE... where
                //A is the index of a child of the root
                //B is the index of a child of the node corresponding to A, etc
                //thus each leaf name encodes a full path from the root to the leaf
                //and the collection of all leaf names encodes the full tree topology
                leafList.LeafNames.Add(name);
            }
        }

        private Mesh MakeLeafMesh(SceneNode leaf, MeshOperator meshOp)
        {
            Mesh leafMesh = null;
           
            if (!leaf.HasComponent<NodeBounds>())
            {
                throw new Exception(string.Format("leaf {0} missing bounds", leaf.Name));
            }

            //clip the big mesh to get a leaf tile's mesh
            try
            {
                leafMesh = meshOp.Clip(leaf.GetComponent<NodeBounds>().Bounds);
            }
            catch (Exception ex)
            {
                pipeline.LogError("error clipping mesh for leaf {0}: {1}", leaf.Name, ex.Message);
                return null;
            }
            
            if (leafMesh.Vertices.Count == 0)
            {
                pipeline.LogWarn("leaf {0} empty", leaf.Name);
                return null;
            }

            //assign UVs to the leaf vertices iff backproject (not baked) texturing is requested
            if (backprojectTextures)
            {
                try
                {
                    leafMesh = UVAtlas.Atlas(leafMesh, options.TextureResolution, options.TextureResolution);
                }
                catch (Exception ex)
                {
                    pipeline.LogError("error atlasing leaf mesh {0}: {1}", leaf.Name, ex.Message);
                    return null;
                }
            }
            
            return leafMesh;
        }

        private Image BackprojectLeafTexture(SceneNode leaf, Mesh leafMesh)
        {
            Image leafImage = null;

            try
            {
                bool logging = pipeline.Verbose || pipeline.Debug;
                var backprojectResults = BackprojectObservations(leafMesh, logging, obsToHull);

                // tile with no textures means it is wholly extrapolation by reconstruction algorithm. skip it.
                if (backprojectResults.Count == 0)
                {
                    return null;
                }

                leafImage = new Image(3, resolution, resolution);
                Backproject.FillOutputTexture(pipeline, backprojectResults, leafImage, options.TextureVariant,
                                              !options.DontInpaint, fallbackToOriginal: true);
            }
            catch (Exception ex)
            {
                pipeline.LogError("error building leaf mesh {0}: {1}", leaf.Name, ex.Message);
                return null;
            }

            return leafImage;
        }
    }
}
