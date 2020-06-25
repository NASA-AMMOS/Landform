using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CommandLine;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Pipeline.Texturing;
using OPS.RayTrace;

/// <summary>
/// Creates a tileset containing a fixed sphere mesh to display behind the terrain.
/// 
/// Runs after build-geometry so that a surface scene mesh exists to use for occlusion (prevents a double image of near
/// geometry in the distant skysphere).  Runs before all surface tiling stages.
///
/// Creates a tiling project, populates it with sky sphere tile geometry, textures the tiles, then destroys the project
/// to leave a clean database for surface tiling.
/// 
/// The outputs match the build-tileset command:
/// * one B3DM file for each tile
/// * one tileset.json file defining the tile hierarchy and a bounds and geometric error for every tile
/// * one stats.txt file containing statistics of the tileset
/// * optionally an additonal mesh and texture file per tile if "export" formats are defined.
///
/// They are written to project storage in the /sky subdirectory of the tiling folder
///
/// Example:
///
/// Landform.exe build-sky-sphere windjana --meshframe 0311472
///
/// </summary>
namespace OPS.Landform
{
    [Verb("build-sky-sphere", HelpText = "build a skysphere tileset from observations")]
    public class BuildSkySphereOptions : TilingCommandOptions
    {
        [Option(HelpText = "Sky sphere radius (meters)", Default = 1000)]
        public double SphereRadiusMeters { get; set; }

        [Option(HelpText = "Sky sphere mesh resolution (degrees)", Default = 10)]
        public double SphereResolutionDegrees { get; set; }

        [Option(HelpText = "A quality/perf tradeoff spent caclulating which texture to use", Default = 4)]
        public double BackprojectSamplesPerTile { get; set; }

        [Option(HelpText = "Sky sphere background color Red (0-255)", Default = 200)]
        public double SkyColorRed { get; set; }

        [Option(HelpText = "Sky sphere background color Green (0-255)", Default = 180)]
        public double SkyColorGreen{ get; set; }

        [Option(HelpText = "Sky sphere background color Blue (0-255)", Default = 140)]
        public double SkyColorBlue { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoTextures { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoIndexImages { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoSave { get; set; }

        [Option(HelpText = "option disabled for this command", Default = false)]
        public override bool NoOrbital { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoSurface { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = 0)]
        public override double TextureFarClip { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = ObsSelectionStrategyName.Spatial)]
        public override ObsSelectionStrategyName ObsSelectionStrategy { get; set; }

        [Option(HelpText = "Intermediate tiling and blending results are deleted by default", Default = false)]
        public bool NoCleanup { get; set; }

        [Option(HelpText = "Write blend debug products", Default = false)]
        public bool DebugBlend { get; set; }
    }

    public class BuildSkySphere : TilingCommand
    {
        public const string SKY_DIR = "sky";

        private BuildSkySphereOptions options;

        private float[] skyColor;

        private Image bigIndexMap;
        private Image bigBlurredImage;
        private Image bigBlendedImage;

        public BuildSkySphere(BuildSkySphereOptions options) : base(options)
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
                 
                //prep
                RunPhase("load input mesh", () => LoadInputMesh(requireUVs: false));
                RunPhase("build occlusion datastructures", BuildSceneCaster);
                RunPhase("filter images for sky images", FilterRoverImages);
                RunPhase("checking/generating observation image masks", BuildObservationImageMasks);
                RunPhase("build observation frustum hulls", BuildObsHulls);

                //build geometry and tiling input
                RunPhase("build sphere tile geometry", BuildSphereTiles);
                RunPhase("build sphere tile textures", BuildTileTextures);

                //blending
                RunPhase("build big index", BuildBigIndex);
                RunPhase("build blurred observations", BuildBlurredObservationImages);
                RunPhase("build big blurred image", BuildBigBlurredImage);
                RunPhase("build big blended image", BuildBigBlendedImage);
                RunPhase("build blended observations", BuildBlendedObservationImages);
                RunPhase("build blended leaf tiles observations", BuildBlendedLeafTextures);

                //build tileset
                RunPhase("create tiling project", CreateTilingProject);
                RunPhase("add tile meshes", AddTileMeshes);
                RunPhase("build tiles and define parents", BuildTilesAndDefineParents);
                RunPhase("build parent tiles", BuildParentTiles);

                if (!options.NoCleanup)
                {
                    RunPhase("delete tiling project and blended observation images", Cleanup);
                }
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
            if (options.NoIndexImages)
            {
                throw new Exception("--noindeximages not implemented for this command");
            }

            if (options.NoSave)
            {
                throw new Exception("--nosave not implemented for this command");
            }

            if (options.NoSurface)
            {
                throw new Exception("--nosurface not implemented for this command");
            }

            if (options.ObsSelectionStrategy != ObsSelectionStrategyName.Spatial)
            {
                throw new Exception("--obsselectionstrategy not implemented for this command");
            }

            //set before calling base.ParseArgumentsAndLoadCaches() to avoid warnings if orbital not available
            options.NoOrbital = true;

            if (!base.ParseArgumentsAndLoadCaches())
            {
                return false; //help
            }

            if (!withTextures)
            {
                throw new Exception("--notextures not implemented for this command");
            }

            PipelineOperation.LessSpew = PipelineStateMachine.LessSpew = !(pipeline.Verbose || pipeline.Debug);
            PipelineOperation.SingleWorkflowSpew = PipelineStateMachine.SingleWorkflowSpew = true;

            tilesetFolder = DecorateOutDir(TilingCommand.TILESET_DIR) + "/" + SKY_DIR;

            //need camera frustums to reach skybox
            options.TextureFarClip = options.SphereRadiusMeters * 2.0;

            //select a good spacing of backproject points per tile
            double lengthOfTile = (options.SphereRadiusMeters * Math.Tan(MathHelper.ToRadians(options.SphereResolutionDegrees))); //ISSUE #1094: only works for small angles, use distance on the surface of sphere
            options.BackprojectQuality = options.BackprojectSamplesPerTile / (lengthOfTile * lengthOfTile);
            options.BackprojectQuality /= 100; //convert to expected units 'quality'

            skyColor = new float[] { (float)options.SkyColorRed / 255.0f,
                                     (float)options.SkyColorGreen / 255.0f,
                                     (float)options.SkyColorBlue / 255.0f };
            return true;
        }

        protected override void LoadTileList()
        {
            return; // LoadTileList() is called from TilingCommand.ParseArgumentsAndLoadCaches()
        }

        protected override void CreateTilingProject()
        {
            CreateTilingProject(TilingScheme.Flat);
        }

        private void Cleanup()
        {
            //remove all side effects from this command

            pipeline.LogInfo("deleting blurred observation images");
            CoreLimitedParallel.ForEach(indexedImages, entry =>
            {
                Observation obs = entry.Value;
                if (obs.BlendedGuid != Guid.Empty)
                {
                    obs.BlendedGuid = Guid.Empty;
                    obs.Save(pipeline);
                }
            });

            pipeline.LogInfo("deleting tiling project");
            tilingProject.Delete(pipeline, ignoreErrors: false, keepTileset: true);
        }

        private void BuildBlendedLeafTextures()
        {
            string leafFolder = DecorateOutDir(TilingCommand.OUT_DIR);
            BlendImages.BuildBlendedLeafTextures(pipeline, project, leafFolder, tileList, indexedImages, orbitalTexture,
                                                 options.BackprojectInpaintMissing, options.BackprojectInpaintGutter,
                                                 skyColor);
        }

        private BlendImagesOptions GetBlendOptions()
        {
            BlendImagesOptions blendOps = new BlendImagesOptions();
            //TODO: copy texture command options from options before overriding blend specific values
            blendOps.BlendStrategy = BlendStrategy.Inpaint;
            blendOps.BarycentricInterpolateWinners = false;
            blendOps.InpaintDiff = -1;
            blendOps.BlurDiff = 7;
            blendOps.NoFillBlendWithAverageDiff = false;
            blendOps.TextureVariant = TextureVariant.Blurred;
            blendOps.ResidualEpsilon = LimberDMG.DEF_RESIDUAL_EPSILON;
            blendOps.NumRelaxationSteps = LimberDMG.DEF_NUM_RELAXATION_STEPS;
            blendOps.NumMultigridIterations = LimberDMG.DEF_NUM_MULTIGRID_ITERATIONS;
            blendOps.Lambda = LimberDMG.DEF_LAMBDA;
            blendOps.RedoBlendedObservationTextures = options.Redo;
            return blendOps;
        }

        private void BuildBlendedObservationImages()
        {
            Action<Image, Observation, string> saveDebugImg = null;
            if (options.DebugBlend)
            {
                saveDebugImg = SaveDebugWedgeImage;
            }
            BlendImages.BuildBlendedObservationImages(pipeline, project, GetBlendOptions(),
                                                      bigBlendedImage.Width, bigBlendedImage.Height,
                                                      bigIndexMap, bigBlendedImage, indexedImages, saveDebugImg);
            bigIndexMap = null;
            bigBlendedImage = null;
        }

        private void BuildBigBlendedImage()
        {
            bigBlendedImage = BlendImages.BlendImage(pipeline, GetBlendOptions(),
                                                     bigBlurredImage.Width, bigBlurredImage.Height, 
                                                     bigIndexMap, bigBlurredImage, indexedImages);
            bigBlurredImage = null; //free memory
        }

        private void FilterRoverImages()
        {

            // raycast the corners for a quick test to see if something that should be in
            // skybox should be visible. this is not a perfect test. It is possible that looking 
            // throught a canyon would have all four corners report they hit the scene mesh and 
            // miss the fact skybox related data would be visible throught the middle of the image.
            roverImages = roverImages.Where(obs =>
            {
                var corners = new Vector2[] { new Vector2(0, 0), new Vector2(obs.Width, 0),
                                              new Vector2(0, obs.Height), new Vector2(obs.Width, obs.Height) };
                var obsToMesh =
                frameCache.GetObservationTransform(obs, meshFrame, options.UsePriors, options.OnlyAligned).Mean;
                return corners.Any(c => !Backproject.RaycastMesh(obs.CameraModel, obsToMesh, c, sceneCaster).HasValue);
            }).ToList();
        }

        private void BuildBigBlurredImage()
        {
            var backprojectResults = Backproject.BuildResultsFromIndex(bigIndexMap, indexedImages);
            bigBlurredImage = new Image(3, bigIndexMap.Width, bigIndexMap.Height);
            Backproject.FillOutputTexture(pipeline, project, backprojectResults, bigBlurredImage,
                                          TextureVariant.Blurred, options.BackprojectInpaintMissing,
                                          options.BackprojectInpaintGutter, missingColor: skyColor);
        }

        //builds a large single image of the backproject results as an index map
        private void BuildBigIndex()
        {
            GetNumSphereTiles(out int rows, out int cols);

            int bigMapWidth = cols * tileResolution;
            int bigMapHeight = rows * tileResolution;

            bigIndexMap = new Image(3, bigMapWidth, bigMapHeight);

            string leafFolder = DecorateOutDir(TilingCommand.OUT_DIR);
            CoreLimitedParallel.ForEach(tileTree.Leaves(), leaf =>
            {
                string indexName = leaf.Name + TileList.INDEX_FILE_SUFFIX + TileList.INDEX_FILE_EXT;
                string indexUrl = pipeline.GetStorageUrl(leafFolder, project.Name, indexName);
                var leafIndex = MaskBackprojectIndex(pipeline.LoadImage(indexUrl));

                //fill small gaps along tile boundaries, should make LimberDMG happier
                //TODO: see if inpaint after instead of here works better
                leafIndex.Inpaint(2, useAnyNeighbor: true);

                //blit into big map
                int tileNum = int.Parse(leaf.Name) - 1; //one based
                int tileRow = tileNum / cols;
                int tileCol = tileNum % cols;
                int dstPixelRow = tileRow * tileResolution;
                int dstPixelCol = tileCol * tileResolution;

                lock (bigIndexMap)
                {
                    bigIndexMap.Blit(leafIndex, dstPixelCol, dstPixelRow);
                }
            });

            //ISSUE #1093 replicate data to avoid seam at the wrapping edge of texture data
        }

        private void GetNumSphereTiles(out int rows, out int cols)
        {
            //only need tiles to cover the lowest point visible from rover height
            //assume from center, angle would be different from the edge, but less savings
            //assumes z down
            BoundingBox sceneBounds = mesh.Bounds();
            Vector3 roverMastLocation = new Vector3(0, 0, -5.0); //TODO: pull from a mastcam z-height in mission specific or expose viewer height
            Vector3 lowestViewVector = Vector3.Normalize(sceneBounds.Max - roverMastLocation);    //z incresases down
            double angleBelowHorizon = lowestViewVector.Z * Math.PI / 2.0; // equivalent to Vector3.Dot(roverMastLocation, new Vector3(0,0,1)) * PI/2

            double sphereResRad = MathHelper.ToRadians(options.SphereResolutionDegrees);
            rows = (int)((Math.PI / 2.0 + angleBelowHorizon) / sphereResRad);
            cols = (int)(2.0 * Math.PI / sphereResRad);
        }

        private void BuildSphereTiles()
        {
            double sphereResRad = MathHelper.ToRadians(options.SphereResolutionDegrees);
            GetNumSphereTiles(out int rows, out int cols);

            //generate the verts to be shared by the tiles

            List<Vector3> positions = new List<Vector3>();
            for (int idxRow = 0; idxRow < rows; idxRow++)
            {
                for (int idxCol = 0; idxCol < cols; idxCol++)
                {
                    double el = Math.PI - idxRow * sphereResRad; //work from top down (want total coverage above, partial below)
                    double az = idxCol * sphereResRad;

                    Vector3 pos = Vector3.Zero;
                    pos.X = options.SphereRadiusMeters * Math.Cos(az) * Math.Sin(el);
                    pos.Y = options.SphereRadiusMeters * Math.Sin(az) * Math.Sin(el);
                    pos.Z = options.SphereRadiusMeters * Math.Cos(el);
                    positions.Add(pos);
                }
            }

            int ToIndex(int row, int col, int numCols)
            {
                return row * cols + col;
            }

            //create mesh tiles
            List<Mesh> tiles = new List<Mesh>();
            for (int idxRow = 1; idxRow < rows; idxRow++)
            {
                int prevRow = idxRow - 1;

                for (int idxCol = 1; idxCol <= cols; idxCol++)
                {
                    int curCol = idxCol;
                    int prevCol = idxCol - 1;

                    if (idxCol == cols)
                    {
                        //wrap
                        curCol = 0;
                        prevCol = cols - 1;
                    }

                    Vector3 topLeft = positions[ToIndex(prevRow, prevCol, cols)];
                    Vector3 topRight = positions[ToIndex(prevRow, curCol, cols)];
                    Vector3 bottomLeft = positions[ToIndex(idxRow, prevCol, cols)];
                    Vector3 bottomRight = positions[ToIndex(idxRow, curCol, cols)];
                    tiles.Add(BuildSphereTile(topLeft, topRight, bottomLeft, bottomRight));
                }
            }

            //create tile tree
            tileTree = DefineTiles.BuildSingleLevelBoundsTree(tiles);
        }

        private Mesh BuildSphereTile(Vector3 topLeft, Vector3 topRight, Vector3 bottomLeft, Vector3 bottomRight)
        {
            Mesh tile = new Mesh(hasNormals: true, hasUVs: true);
            tile.Vertices = new List<Vertex>();
            tile.Vertices.Add(new Vertex(topLeft, -Vector3.Normalize(topLeft), Vector4.One, new Vector2(0.0, 1.0)));
            tile.Vertices.Add(new Vertex(topRight, -Vector3.Normalize(topRight), Vector4.One, new Vector2(1.0, 1.0)));
            tile.Vertices.Add(new Vertex(bottomLeft, -Vector3.Normalize(bottomLeft), Vector4.One, new Vector2(0.0, 0.0)));
            tile.Vertices.Add(new Vertex(bottomRight, -Vector3.Normalize(bottomRight), Vector4.One, new Vector2(1.0, 0.0)));

            //right handed winding from interior
            tile.Faces = new List<Face>();
            tile.Faces.Add(new Face(new int[] { 0, 2, 1 }));
            tile.Faces.Add(new Face(new int[] { 1, 2, 3 }));
            return tile;
        }

        private void BuildTileTextures()
        {
            tileList = new TileList()
            {
                MeshExt = meshExt,
                ImageExt = imageExt,
                MeshFrame = meshFrame,
                HasIndexImages = true,
                TilingScheme = TilingScheme.Flat,
                LeafNames = new List<string>(),
                ParentNames = new List<string>()
            };

            var tilesToTexture = tileTree.DepthFirstTraverse() //TODO: #1096 skip root
                .Where(l => l.HasComponent<MeshImagePair>() && l.GetComponent<MeshImagePair>().Mesh != null)
                .ToList();
            int tileCount = tilesToTexture.Count;

            pipeline.LogInfo("backprojecting {0} tiles, texture resolution {1}", tileCount, tileResolution);

            var backprojectContexts = Backproject.BuildContexts(obsToHull, roverImages, mission, frameCache,
                                                                observationCache, meshFrame, tcopts.UsePriors,
                                                                tcopts.OnlyAligned, msg => pipeline.LogWarn(msg));

            int np = 0, curTileNum = 0, numFailed = 0, numSucceded = 0;
            CoreLimitedParallel.ForEach(tilesToTexture, tile =>
            {
                Interlocked.Increment(ref curTileNum);
                Interlocked.Increment(ref np);

                if (!options.NoProgress)
                {
                    pipeline.LogInfo("texturing and saving tile {0}/{1} ({2:F2}%){3}: {4}",
                                     curTileNum, tileCount, 100 * curTileNum / (float)tileCount,
                                     np > 1 ? ", processing " + np + " in parallel" : "", tile.Name);
                }

                MeshImagePair mp = tile.GetComponent<MeshImagePair>();

                Image index = new Image(3, tileResolution, tileResolution);

                SceneCaster meshCaster = new SceneCaster();
                meshCaster.AddMesh(mesh, null, Matrix.Identity);
                meshCaster.Build();

                ObsSelectionStrategy strategy = ObsSelectionStrategy.Create(options.ObsSelectionStrategy);
                strategy.Initialize(mp.Mesh, new MeshOperator(mp.Mesh), meshCaster, sceneCaster,
                                    options.RaycastTolerance, backprojectContexts, tileResolution,
                                    options.BackprojectQuality);

                mp.Image = BackprojectTile(tile, mp.Mesh, index, meshCaster, strategy);

                if (mp.Image != null)
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
            });                           

            pipeline.LogInfo("backprojected {0} pixels from surface observations, {1} from orbital, {2} failed",
                             Fmt.KMG(numBackprojectedSurfacePixels), Fmt.KMG(numBackprojectedOrbitalPixels),
                             Fmt.KMG(numBackprojectFailedPixels));

            if (numFailed > 0)
            {
                pipeline.LogWarn("failed to generate textures for {0} tiles", numFailed);
            }

            pipeline.LogInfo("{0} tiles built successfully", numSucceded);
            tileTree.DumpStats(msg => pipeline.LogInfo(msg));
        }
    }
}
 
