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
/// creates a tileset containing a fixed sphere mesh to display
/// behind the terrain
/// </summary>
/// 

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
        public override bool NoSave { get; set; }

        [Option(HelpText = "option disabled for this command", Default = false, Required = false)]
        public override bool NoOrbital { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = false, Required = false)]
        public override bool NoSurface { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = 0)]
        public override double TextureFarClip { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = ObsSelectionStrategyName.Spatial)]
        public override ObsSelectionStrategyName ObsSelectionStrategy { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = -1)]
        public override int BackprojectInpaintPixels { get; set; }

        [Option(HelpText = "Tiling project is deleted after run by default, enable this to preserve it for debugging or iteration", Default = false)]
        public bool NoCleanup { get; set; }
    }

    public class BuildSkySphere : TilingCommand
    {
        public const string TILESET_DIR = "tiling/TileSet";
        public const string TILING_DIR_SUFFIX = "_sky";

        private SceneNode tileTree;
        private BuildSkySphereOptions options;
        private List<Backproject.Context> contexts;
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
                RunPhase("create tiling project", () => CreateTilingProject(TilingScheme.Flat));
                RunPhase("add tile meshes", AddTileMeshes);
                RunPhase("build tiles and define parents", BuildTilesAndDefineParents);
                RunPhase("build parent tiles", BuildParentTiles);

                if(!options.NoCleanup)
                {
                    RunPhase("delete tiling project", DeleteTilingProject);
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

        private void DeleteTilingProject()
        {
            pipeline.LogInfo("deleting tiling projcect");
            tilingProject.Delete(pipeline,ignoreErrors:false, keepTileset:true);
        }

        private void BuildBlendedLeafTextures()
        {
            pipeline.LogInfo("blending leaf textures");
            string leafFolder = DecorateOutDir(TilingCommand.OUT_DIR);
            float [] skyColor = new float[] { (float)options.SkyColorRed / 255.0f, (float)options.SkyColorGreen / 255.0f, (float)options.SkyColorBlue / 255.0f };

            BlendImages.BuildBlendedLeafTextures(pipeline, project, leafFolder, tileList, indexedImages, orbitalTexture, options.BackprojectInpaintPixels, skyColor);
        }

        private BlendImagesOptions GetBlendOptions()
        {
            BlendImagesOptions blendOps = new BlendImagesOptions();
            //TODO: copy texture command options from options before overriding blend specific values
            blendOps.BlendStrategy = BlendStrategy.Inpaint;
            blendOps.InpaintWinners = 20;
            blendOps.NoBarycentricInterpolateWinners = true;
            blendOps.InpaintDiff = 0;
            blendOps.BlurDiff = 7;
            blendOps.NoFillBlendWithAverageDiff = false;
            blendOps.TextureVariant = TextureVariant.Blurred;
            blendOps.ResidualEpsilon = LimberDMG.DEF_RESIDUAL_EPSILON;
            blendOps.NumRelaxationSteps = LimberDMG.DEF_NUM_RELAXATION_STEPS;
            blendOps.NumMultigridIterations = LimberDMG.DEF_NUM_MULTIGRID_ITERATIONS;
            blendOps.Lambda = LimberDMG.DEF_LAMBDA;
            return blendOps;
        }

        private void BuildBlendedObservationImages()
        {
            pipeline.LogInfo("collecting backprojected pixels for each observation");
            BlendImages.BuildBlendedObservationImages(pipeline, project, GetBlendOptions(),
                bigBlendedImage.Width, bigBlendedImage.Height,
                bigIndexMap, bigBlendedImage, indexedImages);

            bigIndexMap = null;
            bigBlendedImage = null;
        }

        private void BuildBigBlendedImage()
        {
            BlendImages.BlendImage(pipeline, GetBlendOptions(), bigBlurredImage.Width, bigBlurredImage.Height, 
                bigIndexMap, bigBlurredImage, indexedImages, out bigBlendedImage);

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
                var obsToMesh = frameCache.GetObservationTransform(obs, meshFrame, tcopts.UsePriors, tcopts.OnlyAligned).Mean;
                var cameraModel = obs.CameraModel;

                return (!Backproject.RaycastMesh(cameraModel, obsToMesh, new Vector2(0, 0), sceneCaster).HasValue ||
                       !Backproject.RaycastMesh(cameraModel, obsToMesh, new Vector2(obs.Width, 0), sceneCaster).HasValue ||
                       !Backproject.RaycastMesh(cameraModel, obsToMesh, new Vector2(0, obs.Height), sceneCaster).HasValue ||
                       !Backproject.RaycastMesh(cameraModel, obsToMesh, new Vector2(obs.Width, obs.Height), sceneCaster).HasValue);
            }).ToList();
        }

        private void BuildBigBlurredImage()
        {
            pipeline.LogInfo("building big blurred image from big blurred index");
            var backprojectResults = Backproject.BuildResultsFromIndex(bigIndexMap, indexedImages);
            bigBlurredImage = new Image(3, bigIndexMap.Width, bigIndexMap.Height);
            Backproject.FillOutputTexture(pipeline, backprojectResults, bigBlurredImage, TextureVariant.Blurred, 0, false);
        }

        //builds a large single image of the backproject results as an index map
        private void BuildBigIndex()
        {
            GetNumSphereTiles(out int rows, out int cols);

            int bigMapWidth = cols * resolution;
            int bigMapHeight = rows * resolution;

            bigIndexMap = new Image(3, bigMapWidth, bigMapHeight);

            string leafFolder = DecorateOutDir(TilingCommand.OUT_DIR);
            CoreLimitedParallel.ForEach(tileTree.Leaves(), leaf =>
            {
                string indexName = leaf.Name + TileList.INDEX_FILE_SUFFIX + TileList.INDEX_FILE_EXT;
                string indexUrl = pipeline.GetStorageUrl(leafFolder, project.Name, indexName);
                var leafIndex = Backproject.LoadIndexMapWithMask(pipeline, indexUrl);

                //fill small gaps along tile boundaries, should make LimberDMG happier
                //TODO: see if inpaint after instead of here works better
                leafIndex.Inpaint(2, useAnyNeighbor: true);

                //blit into big map
                int tileNum = int.Parse(leaf.Name) - 1; //one based
                int tileRow = tileNum / cols;
                int tileCol = tileNum % cols;
                int dstPixelRow = tileRow * resolution;
                int dstPixelCol = tileCol * resolution;

                lock (bigIndexMap)
                {
                    bigIndexMap.Blit(leafIndex, dstPixelCol, dstPixelRow);
                }
            });

            //TODO: replicate a column of the one side into the other to prevent a seam

        }

        protected override bool ParseArgumentsAndLoadCaches()
        {
            if (options.NoSave)
            {
                throw new Exception("--nosave not implemented for this command");
            }

            if (options.NoSurface)
            {
                throw new Exception("--nosurface not implemented for this command");
            }

            //set before calling base.ParseArgumentsAndLoadCaches() to avoid warnings if orbital not available
            options.NoOrbital = true;

            if (!base.ParseArgumentsAndLoadCaches())
            {
                return false; //help
            }

            PipelineOperation.LessSpew = PipelineStateMachine.LessSpew = !(pipeline.Verbose || pipeline.Debug);
            PipelineOperation.SingleWorkflowSpew = PipelineStateMachine.SingleWorkflowSpew = true;

            tilesetFolder = DecorateOutDir(TILESET_DIR) + TILING_DIR_SUFFIX;

            //need camera frustums to reach skybox
            options.TextureFarClip = options.SphereRadiusMeters * 2.0;

            //select a good spacing of backproject points per tile
            double lengthOfTile = (options.SphereRadiusMeters * Math.Tan(MathHelper.ToRadians(options.SphereResolutionDegrees)));//bugbug: only works for small angles
            options.BackprojectQuality = options.BackprojectSamplesPerTile / (lengthOfTile * lengthOfTile);
            options.BackprojectQuality /= 100; //convert to expected units 'quality'

            return true;
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

                    //verts are added from the top of the sphere (-Z) first
                    //Vector3 topLeft = positions[ToIndex(idxRow, prevCol, cols)];
                    //Vector3 topRight = positions[ToIndex(idxRow, curCol, cols)];
                    //Vector3 bottomLeft = positions[ToIndex(prevRow, prevCol, cols)];
                    //Vector3 bottomRight = positions[ToIndex(prevRow, curCol, cols)];

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

        Mesh BuildSphereTile(Vector3 topLeft, Vector3 topRight, Vector3 bottomLeft, Vector3 bottomRight)
        {
            Mesh tile = new Mesh(hasNormals: true, hasUVs: true);
            tile.Vertices = new List<Vertex>();
            tile.Vertices.Add(new Vertex(topLeft, -Vector3.Normalize(topLeft), Vector4.One, new Vector2(0.0, 1.0)));
            tile.Vertices.Add(new Vertex(topRight, -Vector3.Normalize(topRight), Vector4.One, new Vector2(1.0, 1.0)));
            tile.Vertices.Add(new Vertex(bottomLeft, -Vector3.Normalize(bottomLeft), Vector4.One, new Vector2(0.0, 0.0)));
            tile.Vertices.Add(new Vertex(bottomRight, -Vector3.Normalize(bottomRight), Vector4.One, new Vector2(1.0, 0.0)));

            //right handed winding from interior
            tile.Faces = new List<Face>();
            //tile.Faces.Add(new Face(new int[] { 0, 1, 2 }));
            //tile.Faces.Add(new Face(new int[] { 2, 1, 3 }));
            tile.Faces.Add(new Face(new int[] { 0, 2, 1 }));
            tile.Faces.Add(new Face(new int[] { 1, 2, 3 }));
            return tile;
        }

        private void BuildTileTextures()
        {
            tileList = new TileList()
            {
                MeshExt = meshExt,
                ImageExt = withTextures ? imageExt : null,
                MeshFrame = meshFrame,
                HasIndexImages = true,
                TilingScheme = TilingScheme.Flat,
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

            var texMsg = string.Format("{0}x{0} {1} textures{2}",
                                       resolution, options.TextureVariant,
                                       options.TextureVariant != TextureVariant.Original ?
                                       " (falling back to " + TextureVariant.Original + ")" : "");
            pipeline.LogInfo("processing {0} tiles{1}", tileCount, ", backprojecting ");
            pipeline.LogInfo("saving tile backproject index images");

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

                MeshImagePair mp = tile.GetComponent<MeshImagePair>();

                Image index = new Image(3, resolution, resolution);
                mp.Image = BackprojectTile(tile, mp.Mesh, index);

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
                
            contexts = Backproject.BuildContexts(obsToHull, roverImages, mission, frameCache,
                                                     observationCache, meshFrame, tcopts.UsePriors,
                                                     tcopts.OnlyAligned, msg => pipeline.LogWarn(msg));


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
        }

        private Image BackprojectTile(SceneNode node, Mesh mesh, Image index)
        {
            try
            {
                List<PixelPoint> missingPixels = null;
                missingPixels = new List<PixelPoint>();

                SceneCaster meshCaster = new SceneCaster();
                meshCaster.AddMesh(mesh, null, Matrix.Identity);
                meshCaster.Build();

                ObsSelectionStrategy obsSelStrat = ObsSelectionStrategy.Create(options.ObsSelectionStrategy);
                obsSelStrat.Initialize(mesh, new MeshOperator(mesh), meshCaster, sceneCaster, contexts, resolution, tcopts.BackprojectQuality);
                IDictionary<Pixel, Backproject.ObsPixel> backprojectResults = BackprojectRoverObservations(mesh, meshCaster, options.TextureResolution, missingPixels,
                                                                    obsSelStrat, debugSubdir: node.Name);

                Image image = new Image(3, resolution, resolution);

                if (index != null)
                {
                    Backproject.FillIndexImage(backprojectResults, index);
                }

                var stats = Backproject.FillOutputTexture(pipeline, backprojectResults, image, options.TextureVariant,
                                                          options.BackprojectInpaintPixels, fallbackToOriginal: true,
                                                          orbitalTexture: orbitalTexture);
                return image;
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex, $"error backprojecting tile {node.Name}");
                return null;
            }
        }
    }
}
 