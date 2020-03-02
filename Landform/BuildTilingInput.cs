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
using OPS.Util;

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

        [Option(HelpText = "Tiling scheme (Bin, QuadX, QuadY, QuadZ, Oct)", Default = TilingScheme.Bin)]
        public TilingScheme TilingScheme { get; set; }

        [Option(Default = "auto", HelpText = "Texture mode (None, Clip, Bake, Backproject, auto)")]
        public string TextureMode { get; set; }

        [Option(HelpText = "Preferred observation image texture variant (Original, Blurred, Blended), falls back to Original", Default = TextureVariant.Blended)]
        public override TextureVariant TextureVariant { get; set; }

        [Option(HelpText = "Don't inpaint output to fill seams and holes when backprojecting", Default = false)]
        public bool DontInpaint { get; set; }

        [Option(HelpText = "Debug function that skips all tiles except the one with this name", Default = null)]
        public string OnlyTilesNamed { get; set; }

        [Option(HelpText = "Mission to use if creating project (only if --inputmesh --inputtexture (or texturing disabled)", Default = Mission.None)]
        public Mission Mission { get; set; }

        [Option(HelpText = "Don't save tile backproject index images", Default = false)]
        public bool NoBackprojectIndexImages { get; set; }

        [Option(HelpText = "Save tile backproject index images previews", Default = false)]
        public bool BackprojectIndexImagePreviews { get; set; }

        [Option(HelpText = "Don't use approximated areas for the tilesplit test", Default = false)]
        public bool NoApproxTileSplit { get; set; }
        [Option(HelpText = "just show list of image observations selected for texturing", Default = false)]
        public bool ListImageObservations { get; set; }

        [Option(HelpText = "Disable generating UVs by texture projection", Default = false)]
        public bool NoTextureProjection { get; set; }

        [Option(HelpText = "Max input texture resolution, should be power of two, negative for unlimited", Default = -1)]
        public override int TextureResolution { get; set; }
    }

    public class BuildTilingInput : TilingCommand
    {
        private BuildTilingInputOptions options;

        private bool tacticalFrame;
        private Matrix? meshToImage = null; //non-null iff texture projection enabled
        private TextureMode textureMode = TextureMode.None;
        private Image sceneTexture;
        private SceneNode tileTree;

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

                if (options.ListImageObservations)
                {
                    RunPhase("list image observations", ListImageObservations);
                    return 0;
                }

                RunPhase("check for projectable texture", SetupTextureProjection);

                bool clipOrBake = textureMode == TextureMode.Clip || textureMode == TextureMode.Bake;

                RunPhase("load input mesh", () => LoadInputMesh(requireUVs: clipOrBake));

                if (clipOrBake)
                {
                    RunPhase("load input image", LoadInputTexture);
                }

                if (textureMode == TextureMode.Backproject)
                {
                    RunPhase("checking/generating observation image masks", BuildObservationImageMasks);
                    RunPhase("build observation frustum hulls", BuildObsHulls);
                }

                if (textureMode == TextureMode.Backproject || TextureSplitEnabled())
                {
                    RunPhase("build occlusion datastructures", BuildSceneCaster);
                }

                RunPhase("build tile tree", BuildTileTree);
                RunPhase("build acceleration datastructures", BuildMeshOperator);

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
                    RunPhase("build backproject strategy", InitBackprojectStrategy);
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

        private void ListImageObservations()
        {
            if (imageObservations != null)
            {
                var allImages = observationCache.GetAllObservations()
                    .Where(obs => ((RoverObservation)obs).ObservationType == RoverProductType.Image)
                    .OrderBy(obs => obs.Name)
                    .ToList();
                pipeline.LogInfo("{0} image observations:", allImages.Count);
                foreach (var obs in allImages.OrderBy(obs => obs.Name))
                {
                    pipeline.LogInfo(obs.Name);
                }
                pipeline.LogInfo("{0} image observations selected for texturing:", imageObservations.Count);
                foreach (var obs in imageObservations.OrderBy(obs => obs.Name))
                {
                    pipeline.LogInfo(obs.Name);
                }
            }
            else
            {
                pipeline.LogInfo("no image observations selected for texturing");
            }
        }
            
        protected override bool ParseArgumentsAndLoadCaches()
        {
            if (!base.ParseArgumentsAndLoadCaches())
            {
                return false; //help
            }

            if (project == null && mission == null)
            {
                throw new Exception("--mission required if project not specified");
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
                if (!withTextures)
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
                throw new Exception(string.Format("--tileresolution must be positive for texture mode {0}",
                                                  textureMode));
            }

            pipeline.LogInfo("texture mode: {0}, tile resolution {1}", textureMode, tileResolution);

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

            if (options.NoTextures || options.TileResolution == 0 || options.TextureMode.ToLower() == "none")
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

        protected override MissionSpecific GetMission()
        {
            return project != null ? MissionSpecific.GetInstance(project.Mission) :
                MissionSpecific.GetInstance(options.Mission);
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

        protected override void HandleSpecialMeshFrames()
        {
            meshFrame = GetMeshFrame();

            if (string.IsNullOrEmpty(meshFrame))
            {
                return;
            }

            meshFrame = meshFrame.ToLower().Trim();

            if (meshFrame == "auto")
            {
                meshFrame = GetAutoMeshFrame();
            }

            if (meshFrame == "tactical")
            {
                tacticalFrame = true;
                meshFrame = "passthrough";
            }
            else
            {
                base.HandleSpecialMeshFrames();
            }
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

        //allow specifying e.g. --inputtexture=foo.png but use e.g. foo.IMG for metadata if it exists
        private string GetInputTexturePDS()
        {
            if (string.IsNullOrEmpty(options.InputTexture))
            {
                return null;
            }
            var pdsExts = StringHelper.ParseExts(mission.GetPDSExts(), bothCases: true);
            if (pdsExts.Any(ext => options.InputTexture.EndsWith(ext)))
            {
                return options.InputTexture;
            }
            foreach (var ext in pdsExts)
            {
                var pdsFile = Path.ChangeExtension(options.InputTexture, ext);
                if (File.Exists(pdsFile))
                {
                    return pdsFile;
                }
            }
            return null;
        }

        private void SetupTextureProjection()
        {
            meshToImage = null; //texture projection disabled

            if (options.NoTextureProjection)
            {
                pipeline.LogInfo("texture projection disabled");
                return;
            }

            string meshFrame = tacticalFrame ? mission.GetTacticalMeshFrame() : this.meshFrame;
            meshFrame = meshFrame.ToLower();

            var allowed = new string[] { "local_level", "sitedrive", "site", "rover", "observation" };
            if (!allowed.Contains(meshFrame))
            {
                pipeline.LogInfo("cannot project texture, unhandled mesh frame {0}", meshFrame);
                return;
            }
            
            if (string.IsNullOrEmpty(options.InputMesh) || string.IsNullOrEmpty(options.InputTexture))
            {
                pipeline.LogInfo("cannot project texture, both --inputmesh and --inputtexture are required");
                return;
            }

            var texId = RoverProductId.Parse(options.InputTexture, mission, throwOnFail: false);
            if (texId == null || !texId.IsSingleFrame() || !texId.IsSingleCamera())
            {
                pipeline.LogInfo("cannot project texture, --inputtexture not recognized as a single frame RDR");
                return;
            }

            var meshId = RoverProductId.Parse(options.InputMesh, mission, throwOnFail: false);
            if (meshId == null || !meshId.IsSingleFrame() || !meshId.IsSingleCamera())
            {
                pipeline.LogInfo("cannot project texture, --inputmesh not recognized as a single frame RDR");
                return;
            }

            if (texId.GetPartialId(mission, includeVariants: false, includeProductType: false) !=
                meshId.GetPartialId(mission, includeVariants: false, includeProductType: false))
            {
                pipeline.LogInfo("cannot project texture, --inputmesh ID does not match --inputtexture ID");
                return;
            }

            var texPDS = GetInputTexturePDS();
            if (string.IsNullOrEmpty(texPDS))
            {
                pipeline.LogInfo("cannot project texture, --inputtexture not PDS and has no PDS sibling");
                return;
            }
            if (texPDS != options.InputTexture)
            {
                pipeline.LogInfo("texture projection: using PDS headers from {0} for input texture {1}",
                                 texPDS, options.InputTexture);
            }

            var texImg = pipeline.LoadImage(texPDS);
            if (!(texImg.Metadata is PDSMetadata))
            {
                pipeline.LogInfo("cannot project texture, --inputtexture does not have PDS metadata");
                return;
            }

            var texParser = new PDSParser((PDSMetadata)(texImg.Metadata));
            if (texParser.CameraModelRefFrame != PDSParser.ReferenceCoordinateFrame.RoverNav)
            {
                pipeline.LogInfo("cannot project texture, --inputtexture not in rover frame");
                return;
            }

            if (new string[] { "rover", "observation" }.Contains(meshFrame))
            {
                meshToImage = Matrix.Identity;
            }
            else
            {
                var rdrs = new List<string>();
                var pdsExts = StringHelper.ParseExts(mission.GetPDSExts(), bothCases: true);

                //first try to load transform from an RDR corresponding exactly to --inputmesh
                string meshDir = Path.GetDirectoryName(options.InputMesh); //empty if no dir
                string meshFile = Path.GetFileNameWithoutExtension(options.InputMesh);
                foreach (var ext in pdsExts)
                {
                    rdrs.Add(Path.Combine(meshDir, meshFile + ext)); //empty dir ok
                }
                
                //next try XYZ and RAS variants of the input mesh in that order
                //if a tactical mesh is reprocessed e.g. due to an improved localization solution
                //the corresponding XYZ will also be updated with matching transforms but the RAS may not be
                //note: dupes in the rdrs list are harmless
                if (meshId.GetProductTypeSpan(out int pts, out int ptl))
                {
                    string pfx = meshId.FullId.Substring(0, pts);
                    string sfx = meshId.FullId.Substring(pts + ptl);
                    string mpt = meshId.FullId.Substring(pts, ptl);
                    string xyz = RoverProduct.ToRDRPoductType(RoverProductType.Points);
                    string ras = RoverProduct.ToRDRPoductType(RoverProductType.Image);
                    foreach (var pt in new string[] { xyz, ras })
                    {
                        if (pt != mpt)
                        {
                            foreach (var ext in pdsExts)
                            {
                                rdrs.Add(Path.Combine(meshDir, pfx + pt + sfx + ext));
                            }
                        }
                    }
                }
                
                //finally try RDRs corresponding exactly to --inputtexture
                string texDir = Path.GetDirectoryName(options.InputTexture); //empty if no dir
                string texFile = Path.GetFileNameWithoutExtension(options.InputTexture);
                foreach (var ext in pdsExts)
                {
                    rdrs.Add(Path.Combine(texDir, texFile + ext)); //empty dir ok
                }
                
                foreach (var rdr in rdrs)
                {
                    if (pipeline.FileExists(rdr))
                    {
                        var meshImg = pipeline.LoadImage(rdr);
                        if (meshImg.Metadata is PDSMetadata)
                        {
                            var meshParser = new PDSParser((PDSMetadata)(meshImg.Metadata));
                            var roverOriginRotation = meshParser.RoverOriginRotation;
                            var originOffset = meshParser.OriginOffset;
                            switch (meshFrame)
                            {
                                case "local_level": case "sitedrive":
                                {
                                    meshToImage = RoverCoordinateSystem.LocalLevelToRover(roverOriginRotation);
                                    break;
                                }
                                case "site":
                                {
                                    meshToImage = RoverCoordinateSystem.SiteToRover(roverOriginRotation, originOffset);
                                    break;
                                }
                            }
                            if (meshToImage.HasValue)
                            {
                                string msg = string.Format("texture projection: " +
                                                           "loaded mesh frame {0} to rover frame transform from {1}",
                                                           meshFrame, rdr);
                                var id = RoverProductId.Parse(rdr, mission, throwOnFail: false);
                                if (id == null ||
                                    id.GetPartialId(mission, includeProductType: false) !=
                                    meshId.GetPartialId(mission, includeProductType: false))
                                {
                                    pipeline.LogWarn(msg + " (fuzzy match)");
                                }
                                else
                                {
                                    pipeline.LogInfo(msg);
                                }
                                break;
                            }
                        }
                    }
                }
            }

            if (meshToImage.HasValue)
            {
                pipeline.LogInfo("enabled texture projection");
            }
            else
            {
                pipeline.LogInfo("cannot project texture, no transform from mesh frame {0} to rover frame", meshFrame);
            }
        }

        private void ProjectTexture(Mesh mesh)
        {
            mesh.ProjectTexture(sceneTexture, removeVertsOutsideView: true, processVertsInParallel: false,
                                meshToImage: meshToImage.Value);
        }

        protected override void AtlasMesh(Mesh mesh, string name = "", int textureResolution = 0)
        {
            if (meshToImage.HasValue)
            {
                pipeline.LogInfo("atlasing {0} mesh ({1} triangles) with texture projection",
                                 name, Fmt.KMG(mesh.Faces.Count));
                ProjectTexture(mesh);
            }
            else
            {
                base.AtlasMesh(mesh, name, textureResolution);
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
                    throw new Exception(string.Format("no {0} scene texture in database", options.TextureVariant));
                }
            }
            else
            {
                throw new Exception("cannot load input texture, no scene mesh in database");
            }
            if (sceneTexture != null)
            {
                pipeline.LogInfo("loaded {0}x{1} scene texture", sceneTexture.Width, sceneTexture.Height);
                if (sceneTextureResolution > 0 &&
                    (sceneTexture.Width > sceneTextureResolution || sceneTexture.Height > sceneTextureResolution))
                {
                    sceneTexture = sceneTexture.ResizeMax(sceneTextureResolution);
                    pipeline.LogInfo("resized scene texture to {0}x{1}, max size {2}",
                                     sceneTexture.Width, sceneTexture.Height, sceneTextureResolution);
                }
            }
        }

        private bool TextureSplitEnabled()
        {
            return options.SplitByTexturePctToTest > 0 && tileResolution > 0 &&
                (textureMode == TextureMode.Backproject || meshToImage.HasValue);
        }

        private void BuildTileTree()
        {
            if (meshLOD.Count > 1)
            {
                pipeline.LogInfo("building tile tree from {0} existing LODs, tiling scheme {1}",
                                 meshLOD.Count, options.TilingScheme);
                tileTree = DefineTiles.BuildTileTreeFromLODs(pipeline, options.TilingScheme, meshLOD);
            }
            else
            {
                SplitByTextureOpts texSplitOpts = null;
                if (TextureSplitEnabled())
                {
                    CameraInstance[] cams = null;
                    if (imageObservations != null && frameCache != null && obsToHull != null)
                    {
                        CameraInstance obsToCam(Observation obs)
                        {
                            var xform = frameCache.GetObservationTransform(obs, meshFrame, options.UsePriors);
                            if (xform == null)
                            {
                                return null;
                            }
                            CameraInstance cam = new CameraInstance();
                            cam.cameraToMesh = xform.Mean;
                            cam.meshToCamera = Matrix.Invert(xform.Mean);
                            cam.cameraModel = obs.CameraModel;
                            cam.hullInMesh = obsToHull[obs.Name];
                            cam.widthPixels = obs.Width;
                            cam.heightPixels = obs.Height;
                            return cam;
                        }
                        cams = imageObservations.Select(obsToCam).ToArray();
                    }
                    else if (meshToImage.HasValue)
                    {
                        var md = (pipeline.LoadImage(GetInputTexturePDS())).Metadata as PDSMetadata;
                        var hullInCam = ConvexHull.FromParams(md.CameraModel, md.Width, md.Height);
                        var cam = new CameraInstance();
                        cam.cameraToMesh = Matrix.Invert(meshToImage.Value);
                        cam.meshToCamera = meshToImage.Value;
                        cam.cameraModel = md.CameraModel;
                        cam.hullInMesh = ConvexHull.Transformed(hullInCam, cam.cameraToMesh);
                        cam.widthPixels = md.Width;
                        cam.heightPixels = md.Height;
                        cams = new CameraInstance[] { cam };
                    }
                    if (cams != null && cams.Length > 0 && sceneCaster != null)
                    {
                        Action<string> progress = null, info = null;
                        if (!options.NoProgress)
                        {
                            progress = msg => pipeline.LogInfo(msg);
                        }
                        info = msg => pipeline.LogInfo(msg);
                        texSplitOpts = new SplitByTextureOpts()
                        {
                            pctPixelsToTest = options.SplitByTexturePctToTest,
                            pctSampledPixelsSatisfied = options.SplitByTexturePctSatisfied,
                            splitPixelTexelRatio = options.SplitByTextureSamplingRatio,
                            useApproximateTileSplit = !options.NoApproxTileSplit,
                            tileResolution = tileResolution,
                            scInMesh = sceneCaster,
                            cameraInstances = cams,
                            maxTextureStretch = maxTextureStretch,
                            progress = progress,
                            info = info
                        };
                    }
                }

                pipeline.LogInfo("building tile tree, tiling scheme {0}, max {1} faces/leaf{2}",
                                 options.TilingScheme, options.FacesPerTile,
                                 texSplitOpts != null ?
                                 (", texture split enabled, leaf texture resolution " + tileResolution) : "");
                tileTree = DefineTiles.BuildTileTreeFromInputs(pipeline, options.TilingScheme, options.FacesPerTile,
                                                               new List<MeshImagePair>() { new MeshImagePair(mesh) },
                                                               texSplitOpts);
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
                else if (!options.NoProgress)
                {
                    pipeline.LogInfo("building leaf mesh {0}/{1} ({2:F2}%): {3}", curLeafNum, leafCount,
                                     100 * curLeafNum / (float)leafCount, leaf.Name);
                }

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
                HasIndexImages = textureMode == TextureMode.Backproject && !options.NoBackprojectIndexImages,
                TilingScheme = options.TilingScheme,
                TextureMode = textureMode,
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
                             options.TextureVariant,
                             options.TextureVariant != TextureVariant.Original ?
                             " (falling back to " + TextureVariant.Original + ")" : "");

            if (meshLOD.Count == 1 && (textureMode == TextureMode.Backproject ||
                                       (textureMode == TextureMode.Clip && !meshToImage.HasValue)))
            {
                pipeline.LogWarn("{0} leaf tile textures but baking parent tile textures", texMsg);
            }

            if (tileList.HasIndexImages)
            {
                pipeline.LogInfo("saving tile backproject index images");
            }

            MultiMeshClipper bakeClipper = null;
            if (textureMode == TextureMode.Bake)
            {
                bakeClipper = new MultiMeshClipper();
                bakeClipper.AddInput(mesh, sceneTexture);
                bakeClipper.InitTextureBaker();
            }

            TexturedMeshClipper texClipper = null;
            if (textureMode == TextureMode.Clip)
            {
                texClipper = new TexturedMeshClipper(logger: pipeline);
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
                if (textureMode == TextureMode.Bake)
                {
                    var newMP = bakeClipper.BakeTexture(mp.Mesh, tileResolution, maxTextureStretch,
                                                        msg => pipeline.LogVerbose(msg));
                    if (newMP != null)
                    {
                        mp.Mesh = newMP.Mesh;
                        mp.Image = newMP.Image;
                    }
                }
                else if (textureMode == TextureMode.Backproject)
                {
                    index = !options.NoBackprojectIndexImages ? new Image(3, tileResolution, tileResolution) : null;
                    mp.Image = BackprojectTile(tile, mp.Mesh, index);
                }
                else if (textureMode == TextureMode.Clip)
                {
                    var newMP = texClipper.RemapMeshClipImage(mp.Mesh, sceneTexture, tileResolution);
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

                if (meshToImage.HasValue)
                {
                    pipeline.LogInfo("saving texture projector");
                    var textureProjector = new TextureProjector(sceneTexture, meshToImage.Value);
                    if (textureMode == TextureMode.Clip && meshLOD.Count == 1)
                    {
                        pipeline.LogInfo("saving input image for clipping parent textures");
                        var textureProd = new PngDataProduct(sceneTexture);
                        pipeline.SaveDataProduct(project, textureProd);
                        textureProjector.TextureGuid = textureProd.Guid;
                    }
                    pipeline.SaveDataProduct(project, textureProjector);
                    sceneMesh.TextureProjectorGuid = textureProjector.Guid;
                }
                else
                {
                    sceneMesh.TextureProjectorGuid = Guid.Empty;
                }

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

            if (textureMode == TextureMode.Bake || textureMode == TextureMode.Backproject)
            {
                if (!tileMesh.HasUVs || options.RedoTileMeshUVs)
                {
                    if (meshToImage.HasValue)
                    {
                        pipeline.LogVerbose("(re-)atlasing tile mesh {0} with texture projection", tile.Name);
                        ProjectTexture(tileMesh);
                        tileMesh.RescaleUVsForTexture(tileResolution, tileResolution);
                    }
                    else
                    {
                        pipeline.LogVerbose("(re-)atlasing tile mesh {0} with UVAtlas, texture resolution {1}",
                                            tile.Name, tileResolution);
                        try
                        {
                            tileMesh = UVAtlas.Atlas(tileMesh, tileResolution, tileResolution,
                                                     maxStretch: maxTextureStretch, logger: pipeline);
                            if (tileMesh == null)
                            {
                                throw new Exception("unknown error");
                            }
                        }
                        catch (Exception ex)
                        {
                            pipeline.LogError("error atlasing tile mesh {0} with UVAtlas: {1}", tile.Name, ex.Message);
                            return null;
                        }
                    }
                }
                else
                {
                    pipeline.LogVerbose("using existing UVs on tile {0}", tile.Name);
                    tileMesh.RescaleUVsForTexture(tileResolution, tileResolution);
                }
            }
            else if (textureMode == TextureMode.Clip)
            {
                if ((!tileMesh.HasUVs || options.RedoTileMeshUVs) && meshToImage.HasValue)
                {
                    pipeline.LogVerbose("(re-)atlasing tile mesh {0} with texture projection", tile.Name);
                    ProjectTexture(tileMesh);
                }
                else if (!tileMesh.HasUVs)
                {
                    pipeline.LogError("cannot clip texture for tile {0}: " +
                                      "scene mesh missing UVs and texture projection disabled", tile.Name);
                    return null;
                }
            }

            return tileMesh;
        }

        private Image BackprojectTile(SceneNode node, Mesh mesh, Image index = null)
        {
            try
            {
                var backprojectResults = BackprojectObservations(mesh, tileResolution, debugSubdir: node.Name);

                // tile with no textures means it is wholly extrapolation by reconstruction algorithm. skip it.
                if (backprojectResults.Count == 0)
                {
                    return null;
                }

                if (index != null)
                {
                    Backproject.FillIndexImage(backprojectResults, index);
                }

                Image image = new Image(3, tileResolution, tileResolution);
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
