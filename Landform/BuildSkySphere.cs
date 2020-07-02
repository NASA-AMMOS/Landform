//https://github.jpl.nasa.gov/OnSight/Landform/issues/1095
#define HACK_ROOT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Xna.Framework;
using CommandLine;
using OPS.Util;
using OPS.MathExtensions;
using OPS.RayTrace;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Pipeline.Texturing;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;

/// <summary>
/// Creates a sky tileset to display behind the terrain.
///
/// The sky geometry can either be a portion of a sphere (when run with --nomatchscenebounds) or the vertical sides of a
/// box which approximately match the scene bounds.
/// 
/// Typically runs anytime after build-geometry so that a surface scene exists.  Can be run anytime after ingest without
/// --sceneoccludessky and with --nomatchscenebounds.
///
/// Typical resolution is 5 rows and 32 columns of tiles, each with a 512x512 image
///
/// The output tileset is saved to project storage and will typically contain:
/// * one B3DM file for each tile
/// * one tileset.json file defining the tile hierarchy and a bounds and geometric error for every tile
/// * one stats.txt file containing statistics of the tileset.
///
/// Note: this is also a relatively fast way to generate a large, aligned, blended image panorama.  The tile texture
/// images are spatially coherent in that the tiles are quads with trivial texture coordinates.  They could be loaded on
/// their own as a cylindrical or spherical projection 2D panorama.  With the typical settings giving 5x32 tiles with
/// 512x512 textures the total panorama size is 16k by 2560.
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
        [Option(HelpText = "Sky sphere radius (meters), or auto to fit scene bounds", Default = "auto")]
        public string SphereRadiusMeters { get; set; }

        [Option(HelpText = "Sky sphere mesh tile size (degrees)", Default = 10)]
        public double SphereResolutionDegrees { get; set; }

        [Option(HelpText = "Sky sphere mesh max degrees above / below horizon", Default = 40)]
        public double MaxDegreesFromHorizon { get; set; }

        [Option(HelpText = "Sky sphere mesh extra degrees below horizon in addition to visibility angle from mast to bottom of mesh", Default = 5)]
        public double ExtraDegreesBelowHorizon { get; set; }

        [Option(HelpText = "A quality/perf tradeoff spent caclulating which texture to use", Default = 4)]
        public double BackprojectSamplesPerTile { get; set; }

        [Option(HelpText = "Sky sphere background color Red (0-255)", Default = 200)]
        public double SkyColorRed { get; set; }

        [Option(HelpText = "Sky sphere background color Green (0-255)", Default = 180)]
        public double SkyColorGreen{ get; set; }

        [Option(HelpText = "Sky sphere background color Blue (0-255)", Default = 140)]
        public double SkyColorBlue { get; set; }

        [Option(HelpText = "Disable image blending", Default = false)]
        public bool NoBlend { get; set; }

        [Option(HelpText = "Don't attempt to match the sphere geometry to the scene bounds", Default = false)]
        public bool NoMatchSceneBounds { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoTextures { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoIndexImages { get; set; }

        [Option(HelpText = "option disabled for this command", Default = false)]
        public override bool NoOrbital { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoSurface { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = 0)]
        public override double TextureFarClip { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = ObsSelectionStrategyName.Spatial)]
        public override ObsSelectionStrategyName ObsSelectionStrategy { get; set; }
    }

    public class BuildSkySphere : TilingCommand
    {
        public const string SKY_TILING_DIR = "tiling/SkyTile";
        public const string SKY_TILESET_DIR = "tiling/SkyTileSet";

        private BuildSkySphereOptions options;

        private double sphereRadius;
        private double angleAboveHorizon, angleBelowHorizon;

        private int sphereTileRows, sphereTileCols;

        private float[] skyColor;

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

                if (roverImages.Count == 0)
                {
                    pipeline.LogWarn("no sky observations available");
                    StopStopwatch();
                    return 0;
                }
                 
                RunPhase("checking/generating observation image masks", BuildObservationImageMasks);
                RunPhase("build observation frustum hulls", BuildObsHulls);
                RunPhase("build sky sphere tile geometry", BuildTileTree);
                RunPhase("build sky sphere tile textures", BuildTileTexturesAndSaveTiles);

                if (!options.NoBlend)
                {
                    RunPhase("build blurred observations", BuildBlurredObservationImages);
                    RunPhase("blending sky sphere tile textures", BlendTileTextures);
                }

                if (!options.NoSave)
                {
                    RunPhase("saving sky sphere tileset", SaveTileset);
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

        private bool ParseArgumentsAndLoadCaches()
        {
            if (options.NoIndexImages)
            {
                throw new Exception("--noindeximages not implemented for this command");
            }

            if (options.NoSurface)
            {
                throw new Exception("--nosurface not implemented for this command");
            }

            if (options.ObsSelectionStrategy != ObsSelectionStrategyName.Spatial)
            {
                throw new Exception("--obsselectionstrategy not implemented for this command");
            }

            if (options.TextureFarClip != 0)
            {
                throw new Exception("--texturefarclip not implemented for this command");
            }

            //set before calling base.ParseArgumentsAndLoadCaches() to avoid warnings if orbital not available
            options.NoOrbital = true;

            if (!base.ParseArgumentsAndLoadCaches(SKY_TILING_DIR))
            {
                return false; //help
            }

            if (!withTextures)
            {
                throw new Exception("--notextures not implemented for this command");
            }

            if (!options.NoMatchSceneBounds && (sceneMesh == null || !sceneMesh.GetBounds().HasValue))
            {
                throw new Exception("must run after build-geometry without --nomatchscenebounds");
            }

            if (options.SphereRadiusMeters.ToLower() == "auto")
            {
                var sceneBounds = sceneMesh.GetBounds().Value;
                sphereRadius = Math.Max(sceneBounds.Min.XY().Length(), sceneBounds.Max.XY().Length());
            }
            else
            {
                sphereRadius = double.Parse(options.SphereRadiusMeters);
            }
            pipeline.LogInfo("sky sphere radius {0:f3}m", sphereRadius);

            //need camera frustums to reach sky sphere
            options.TextureFarClip = sphereRadius * 2;

            //only need tiles to cover the lowest point visible from rover height
            //assume from center, angle would be different from the edge, but less savings
            //mission surface frames are X north, Y east, Z down
            angleBelowHorizon = MathHelper.ToRadians(options.ExtraDegreesBelowHorizon);
            if (sceneMesh != null)
            {
                Vector3 roverMastLocation = new Vector3(0, 0, -mission.GetMastHeightMeters());
                angleBelowHorizon += sceneMesh.GetBounds().Value.GetCorners().Max(c =>
                {
                    Vector3 mastToCorner = c - roverMastLocation;
                    return Math.Asin(mastToCorner.Z / mastToCorner.Length());
                });
            }

            double maxAngle = MathHelper.ToRadians(options.MaxDegreesFromHorizon);
            angleBelowHorizon = Math.Min(maxAngle, Math.Max(angleBelowHorizon, 0));
            angleAboveHorizon = maxAngle;

            double tileSizeRad = MathHelper.ToRadians(options.SphereResolutionDegrees);
            sphereTileRows = (int)Math.Ceiling((angleBelowHorizon + angleAboveHorizon) / tileSizeRad);
            sphereTileCols = (int)Math.Ceiling(2 * Math.PI / tileSizeRad);
            if (!options.NoMatchSceneBounds)
            {
                //round up to nearest multiple of 4
                int remainder = sphereTileCols % 4;
                if (remainder > 0)
                {
                    sphereTileCols += 4 - remainder;
                }
            }
            int numTiles = sphereTileRows * sphereTileCols;

            pipeline.LogInfo("creating {0} {1:f3}x{1:f3} deg sky sphere tiles in {2} rows, {3} cols, " +
                             "min elevation {4:f3} deg below horizon, max elevation {5:f3} deg above horizon",
                             numTiles, options.SphereResolutionDegrees, sphereTileRows, sphereTileCols,
                             MathHelper.ToDegrees(angleBelowHorizon), MathHelper.ToDegrees(angleAboveHorizon));

            //length of circular arc = circumference * (angle of arc in radians) / (2 * PI)
            //                       = (2 * PI * radius) * (angle of arc in radians) / (2 * PI)
            //                       = radius * (angle of arc in radians)
            double tileWidthOnSphereAtHorizon = sphereRadius * tileSizeRad;
            double tileAreaOnSphereAtHorizon = tileWidthOnSphereAtHorizon * tileWidthOnSphereAtHorizon;

            //select a good spacing of backproject points per tile
            options.BackprojectQuality = options.BackprojectSamplesPerTile / tileAreaOnSphereAtHorizon;
            options.BackprojectQuality /= ObsSelectionSpatial.QUALITY_TO_SAMPLES_PER_SQUARE_METER; 

            pipeline.LogInfo("backproject quality: {0:f6} ({1} samples per {2:f3}m^2 tile)",
                             options.BackprojectQuality, options.BackprojectSamplesPerTile, tileAreaOnSphereAtHorizon);

            skyColor = new float[] { (float)options.SkyColorRed / 255.0f,
                                     (float)options.SkyColorGreen / 255.0f,
                                     (float)options.SkyColorBlue / 255.0f };

            tilesetFolder = DecorateOutDir(SKY_TILESET_DIR);

            return true;
        }

        protected override void FilterRoverImages()
        {
            base.FilterRoverImages();

            if (sceneCaster == null)
            {
                //FilterRoverImages() is a callback from TextureCommand.ParseArgumentsAndLoadCaches()
                //normally these things would be done later on
                //but we need to get them done sooner here so that we can get the sceneCaster
                if (sceneMesh == null) //might have already been loaded in GetProject()
                {
                    sceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame);
                }
                if (sceneMesh != null)
                {
                    LoadInputMesh(requireUVs: false);
                    BuildSceneCaster();
                }
            }

            if (sceneCaster == null)
            {
                pipeline.LogWarn("no scene mesh, run after build-geometry to filter rover images containing sky");
                return;
            }

            int numWas = roverImages.Count;
            roverImages = roverImages.Where(obs =>
            {
                // raycast the corners for a quick test to see if something that should be in
                // skybox should be visible. this is not a perfect test. It is possible that looking 
                // throught a canyon would have all four corners report they hit the scene mesh and 
                // miss the fact skybox related data would be visible throught the middle of the image.
                var corners = new Vector2[] { new Vector2(0, 0), new Vector2(obs.Width, 0),
                                              new Vector2(0, obs.Height), new Vector2(obs.Width, obs.Height) };
                var obsToMesh =
                frameCache.GetObservationTransform(obs, meshFrame, options.UsePriors, options.OnlyAligned).Mean;
                
                return corners.Any(c => !Backproject.RaycastMesh(obs.CameraModel, obsToMesh, c, sceneCaster).HasValue);
            }).ToList();

            pipeline.LogInfo("filtered {0} rover images to {1} containing sky", numWas, roverImages.Count);
        }

        private void BuildTileTree()
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

            var root = new SceneNode("root");
            var rootBounds = BoundingBoxExtensions.CreateEmpty();
            root.AddComponent<NodeGeometricError>(new NodeGeometricError(2 * sphereRadius));
            tileList.ParentNames.Add(root.Name);

            //mission surface frames are X north, Y right, Z down
            //sphere tile rows decrease in elevation from top down
            //sphere tile cols increase in azimuth clockwise from east 
            //this way they can be blitted to a big image nicely for blending

            Func<double, double, Vector3> azElToXYZ = (az, el) =>
            {
                double projected = sphereRadius * Math.Cos(el);
                return new Vector3(-projected * Math.Sin(az), projected * Math.Cos(az), -sphereRadius * Math.Sin(el));
            };

            if (!options.NoMatchSceneBounds)
            {
                // ulc----D----urc
                //  |           |
                //  |     X     E
                //  |     |     |
                //  C     +-Y  mrc
                //  |           |
                //  |           A
                //  |           |
                // llc----B----lrc

                var sceneBounds = sceneMesh.GetBounds().Value;
                Vector2 llc = sceneBounds.Min.XY();
                Vector2 urc = sceneBounds.Max.XY();
                Vector2 ulc = new Vector2(urc.X, llc.Y);
                Vector2 lrc = new Vector2(llc.X, urc.Y);
                Vector2 mrc = new Vector2(0, urc.Y);

                Func<double, double, double, double> bracket = (min, max, val) => (val - min) / (max - min);

                azElToXYZ = (az, el) =>
                {
                    az = MathE.NormalizeAngle(az);
                    var xy = Vector2.Zero;
                    if (az < 0.25 * Math.PI) //A
                    {
                        xy = Vector2.Lerp(mrc, lrc, bracket(0, 0.25 * Math.PI, az));
                    }
                    else if (az < 0.75 * Math.PI) //B
                    {
                        xy = Vector2.Lerp(lrc, llc, bracket(0.25 * Math.PI, 0.75 * Math.PI, az));
                    }
                    else if (az < 1.25 * Math.PI) //C
                    {
                        xy = Vector2.Lerp(llc, ulc, bracket(0.75 * Math.PI, 1.25 * Math.PI, az));
                    }
                    else if (az < 1.75 * Math.PI) //D
                    {
                        xy = Vector2.Lerp(ulc, urc, bracket(1.25 * Math.PI, 1.75 * Math.PI, az));
                    }
                    else //E
                    {
                        xy = Vector2.Lerp(urc, mrc, bracket(1.75 * Math.PI, 2 * Math.PI, az));
                    }
                    double z = -sphereRadius * (2 * MathE.Clamp01(bracket(-0.5 * Math.PI, 0.5 * Math.PI, el)) - 1);
                    return new Vector3(xy.X, xy.Y, z);
                };
            }

            double azStep = 2 * Math.PI / sphereTileCols;
            double elStep = (angleBelowHorizon + angleAboveHorizon) / sphereTileRows;

            for (int row = 0; row < sphereTileRows; row++)
            {
                for (int col = 0; col < sphereTileCols; col++)
                {
                    double leftAz = col * azStep;
                    double rightAz = leftAz + azStep;
                    double topEl = angleAboveHorizon - row * elStep;
                    double bottomEl = topEl - elStep;

                    var bl = azElToXYZ(leftAz, bottomEl);
                    var br = azElToXYZ(rightAz, bottomEl);
                    var tr = azElToXYZ(rightAz, topEl);
                    var tl = azElToXYZ(leftAz, topEl);

                    var mesh = new Mesh(hasNormals: true, hasUVs: true, capacity: 4);
                    mesh.Vertices.Add(new Vertex(bl, -Vector3.Normalize(bl), Vector4.One, new Vector2(0.0, 0.0)));
                    mesh.Vertices.Add(new Vertex(br, -Vector3.Normalize(br), Vector4.One, new Vector2(1.0, 0.0)));
                    mesh.Vertices.Add(new Vertex(tr, -Vector3.Normalize(tr), Vector4.One, new Vector2(1.0, 1.0)));
                    mesh.Vertices.Add(new Vertex(tl, -Vector3.Normalize(tl), Vector4.One, new Vector2(0.0, 1.0)));
                    
                    //right handed winding from interior
                    mesh.Faces.Add(new Face(new int[] { 0, 1, 2 }));
                    mesh.Faces.Add(new Face(new int[] { 0, 2, 3 }));

                    var corners = new Vector3[] { bl, br, tl, tr };

                    var leaf = new SceneNode((row * sphereTileCols + col).ToString(), root.Transform);
                    leaf.AddComponent(new MeshImagePair(mesh));
                    leaf.AddComponent(new NodeBounds(BoundingBox.CreateFromPoints(corners)));
                    leaf.AddComponent<NodeGeometricError>().Error = 0;
                    //leaf name will be added to tileList.LeafNames in SaveTile()

                    BoundingBoxExtensions.Extend(ref rootBounds, corners);
                }
            }

            root.AddComponent(new NodeBounds(rootBounds));
            tileTree = root;
        }

        private void BuildTileTexturesAndSaveTiles()
        {
            var leaves = tileTree.Leaves().ToList();

            pipeline.LogInfo("backprojecting {0} tiles, texture resolution {1}", leaves.Count, tileResolution);

            var backprojectContexts = Backproject.BuildContexts(obsToHull, roverImages, mission, frameCache,
                                                                observationCache, meshFrame, tcopts.UsePriors,
                                                                tcopts.OnlyAligned, msg => pipeline.LogWarn(msg));

            int np = 0, curTileNum = 0, numFailed = 0, numSucceded = 0;
            CoreLimitedParallel.ForEach(leaves, tile =>
            {
                Interlocked.Increment(ref curTileNum);
                Interlocked.Increment(ref np);

                if (!options.NoProgress)
                {
                    pipeline.LogInfo("texturing and saving tile {0}/{1} ({2:F2}%){3}: {4}",
                                     curTileNum, leaves.Count, 100 * curTileNum / (float)(leaves.Count),
                                     np > 1 ? ", processing " + np + " in parallel" : "", tile.Name);
                }

                var mip = tile.GetComponent<MeshImagePair>();

                var meshCaster = new SceneCaster();
                meshCaster.AddMesh(mip.Mesh, null, Matrix.Identity);
                meshCaster.Build();

                var strategy = ObsSelectionStrategy.Create(options.ObsSelectionStrategy);
                strategy.Initialize(mip.Mesh, new MeshOperator(mip.Mesh), meshCaster, sceneCaster,
                                    options.RaycastTolerance, backprojectContexts, tileResolution,
                                    options.BackprojectQuality);

                mip.Index = new Image(3, tileResolution, tileResolution);
                mip.Image = BackprojectTile(tile, mip.Mesh, mip.Index, meshCaster, strategy);

                if (mip.Image != null)
                {
                    SaveTile(tile.Name, mip.Mesh, mip.Image, mip.Index, localSave, cloudSave, tile.IsLeaf);
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

        private void BlendTileTextures()
        {
            int bigImgWidth = sphereTileCols * tileResolution;
            int bigImgHeight = sphereTileRows * tileResolution;

            Image bigIndexMap = new Image(3, bigImgWidth, bigImgHeight);
            CoreLimitedParallel.ForEach(tileList.LeafNames, leafName =>
            {
                string indexName = leafName + TileList.INDEX_FILE_SUFFIX + TileList.INDEX_FILE_EXT;
                string indexUrl = pipeline.GetStorageUrl(outputFolder, project.Name, indexName);
                var leafIndex = MaskBackprojectIndex(pipeline.LoadImage(indexUrl));

                //fill small gaps along tile boundaries, should make LimberDMG happier
                //TODO: see if inpaint after instead of here works better
                leafIndex.Inpaint(2, useAnyNeighbor: true);

                //blit into big map
                int tileNum = int.Parse(leafName);
                int tileRow = tileNum / sphereTileCols;
                int tileCol = tileNum % sphereTileCols;
                int dstPixelRow = tileRow * tileResolution;
                int dstPixelCol = tileCol * tileResolution;

                lock (bigIndexMap)
                {
                    bigIndexMap.Blit(leafIndex, dstPixelCol, dstPixelRow);
                }
            });

            if (options.WriteDebug)
            {
                SaveBackprojectIndexDebug(bigIndexMap, withMesh: false);
            }

            //ISSUE #1093 replicate data to avoid seam at the wrapping edge of texture data

            var backprojectResults = Backproject.BuildResultsFromIndex(bigIndexMap, indexedImages);

            Image bigBlurredImage = new Image(3, bigImgWidth, bigImgHeight);
            Backproject.FillOutputTexture(pipeline, project, backprojectResults, bigBlurredImage,
                                          TextureVariant.Blurred, options.BackprojectInpaintMissing,
                                          options.BackprojectInpaintGutter, missingColor: skyColor);

            if (options.WriteDebug)
            {
                SaveBackprojectTextureDebug(bigBlurredImage, TextureVariant.Blurred, withMesh: false);
            }

            Image bigBlendedImage = BlendImages.BlendImage(pipeline, bigIndexMap, bigBlurredImage, indexedImages);
            bigBlurredImage = null; //free memory

            if (options.WriteDebug)
            {
                SaveBackprojectTextureDebug(bigBlendedImage, TextureVariant.Blended, withMesh: false);
            }

            var blendOptions = new BlendImagesOptions()
            {
                NoSave = options.NoSave,
                NoProgress = options.NoProgress,
                RedoBlendedObservationTextures = true, //yes, always redo
                BlendStrategy = BlendStrategy.Inpaint,
                BarycentricInterpolateWinners = false,
                //BarycentricInterpolateMaxTriangleSideLengthPixels = 100,
                InpaintDiff = -1,
                BlurDiff = 7,
                NoFillBlendWithAverageDiff = false
            };

            Action<Image, Observation, string> saveDebugImg = null;
            if (options.WriteDebug)
            {
                blendOptions.WriteDebug = true;
                saveDebugImg = SaveDebugWedgeImage;
            }

            BlendImages.BuildBlendedObservationImages(pipeline, project, blendOptions, bigIndexMap, bigBlendedImage,
                                                      indexedImages, TextureVariant.SkyBlended, saveDebugImg);
            bigIndexMap = null;
            bigBlendedImage = null;

            BlendImages.BuildBlendedLeafTextures(pipeline, project, outputFolder, tileList, indexedImages,
                                                 orbitalTexture, options.BackprojectInpaintMissing,
                                                 options.BackprojectInpaintGutter, TextureVariant.SkyBlended);
        }

        protected override void SaveTileset()
        {
            string tsMeshExt = TilingProject.ToExt(TilingProject.DEF_TILESET_MESH_FORMAT);
            Func<SceneNode, string> nodeToUrl = node => node.Name + tsMeshExt;
#if HACK_ROOT
            nodeToUrl = node => (node.Name == "root" ? "0" : node.Name) + tsMeshExt;
            tileTree.AddComponent(new MeshImagePair());
#endif
            SaveTileset(project.Name, nodeToUrl);
        }
    }
}
 
