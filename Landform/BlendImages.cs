//#define DBG_DIFF
using System;
using System.Collections.Generic;
using CommandLine;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.MathExtensions;
using JPLOPS.Imaging;
using JPLOPS.Geometry;
using JPLOPS.Pipeline;
using JPLOPS.Pipeline.AlignmentServer;

/// <summary>
/// Creates blended observation images, implementing the blend-images stage in the Landform contextual mesh workflow.
///
/// Uses LimberDMG to reduce the visibility of seams in a tileset textured by backprojecting multiple observation
/// images. Also see LimberDMGDriver.cs.
///
/// Background:
///
/// The blend-images stage is typically run after build-tiling-input, but it can also be run after build-geometry or
/// build-texture.  Running after build-gemetry corresponds more directly to the legacy approach in TerrainTools for MSL
/// OnSight.  It is also acceptable to skip the blend-images stage to build a tileset without blended textures.
///
/// When run after build-tiling-input, the leaf tile meshes are loaded, textured with the per-leaf backproject index
/// images, and rasterized in a birds eye view generating a coherent scene backproject index image typically with 4k
/// resolution. Each pixel in the index image refers to a pixel in one of the source observation images, i.e. its 3
/// components are (observation index, observation pixel row, observation pixel column).  It is coherent in the sense
/// that adjacent pixels are also at least approximately spatially adjacent, which is not generally true for an atlased
/// texture.  At least approximate spatial coherence is required for LimberDMG.
///
/// When run after build-geometry, the scene mesh is loaded and shrinkwrapped, and the shrinkwrapped mesh is
/// backprojected to generate the coherent scene backproject index image, also typically with 4k resolution.
///
/// When run after build-texture, the scene mesh is loaded and the existing backproject index is reprojected in a birds
/// eye view in a similar approach to the method used when running after build-tiling-input to produce a coherent scene
/// backproject index.  Note that in this case the existing backproject index may have a different resolution and it may
/// be atlased (not spatially coherent).
///
/// However the coherent full-scene backproject index is made, a corresponding coherent full-scene texture is then built
/// from it but using a blurred version of the observation images to remove high frequency comonents so that LimberDMG
/// does not attempt to blend small variations along image seams.
///
/// LimberDMG is then run on the blurred full-scene coherent texture and index.  The index tells LimberDMG where the
/// seams between source images are, and the texture gives the pixel values to be blended.
///
/// LimberDMG returns a blended version of the full-scene coherent texture.  The pixels in this image correspond to a
/// sampling of pixels from orginal observation images, with sparsity and coverage that depends on how backproject
/// selected images and also on the resolution of the full-scene coherent texture.  When compared to the original values
/// at the same locations in the blurred observation images, these samples indicate how to adjust that region of the
/// original observation image.
///
/// We then interpolate these adjustments across the original observation images and save those new images as a
/// "Blended" variant of the observation images.  Several strategies to perform this interopolation are implemented
/// using various combinations of inpaint, barycentric interpolation, and blurring.  A default strategy is automatically
/// selected depending on where in the workflow blend-images is run.  Typically the strategy includes a full inpainting
/// of the adjustment values across the entire observation images, because the sampled pixel locations used for blending
/// may differ significantly from those ultimately used for backprojecting the scene texture.  For example, this can be
/// due to using a coarser resolution (e.g. 4k) for the blend texture vs the final scene texture which could be e.g. 8k
/// if making a monolithic output mesh, or variable if making a tileset.
///
/// If run before build-tiling-input, that stage will detect that blended variants of (at least some) observation images
/// are available, and use them to build the leaf tile textures.
///
/// If run after build-tiling-input the existing leaf tile textures are directly replaced with blended versions.
///
/// If run after build-texture the blended scene texture is saved to project storage and referenced from the SceneMesh
/// database object.  This is not the coherent blended scene texture, but rather a different version of the blended
/// scene texture that is generated to match the existing scene texture resolution and UV atlas.
///
/// A textured scene mesh is also optionally saved to the location given by the second positional command line
/// parameter, which has similar semantics to the --outputmesh option in build-geometry. The blended texture is saved as
/// its sibling.  When run after build-geometry this will be the shrinkwrap mesh textured with the coherent blended
/// scene texture.  When run after build-texture this will be the original scene mesh textured with a version
/// of the blended scene texture that is generated to match the existing scene texture resolution and UV atlas.  Saving
/// the mesh is not supported when run after build-tiling-input, except in the unusual case that build-texture has also
/// already been run.
///
/// Example:
///
/// Landform.exe blend-images windjana
///
/// </summary>
namespace JPLOPS.Landform
{
    public enum BlendStrategy { None, Auto, Barycentric, Inpaint };

    [Verb("blend-images", HelpText = "blend observation images")]
    [EnvVar("BLEND")]
    public class BlendImagesOptions : TextureCommandOptions
    {
        [Value(1, Required = false, HelpText = "URL, file, or file type (extension starting with \".\") to which to save textured scene mesh", Default = null)]
        public string OutputMesh { get; set; }

        [Option(HelpText = "Don't use existing leaves to build backproject index", Default = false)]
        public bool NoUseExistingLeaves { get; set; }

        [Option(HelpText = "Don't use existing backproject index", Default = false)]
        public bool NoUseExistingIndex { get; set; }

        [Option(HelpText = "Scene mesh texture resolution, should be power of two", Default = TexturingDefaults.BLEND_TEXTURE_RESOLUTION)]
        public override int TextureResolution { get; set; }

        [Option(HelpText = "Canned blend strategy (Default, Barycentric, Inpaint)", Default = BlendStrategy.Auto)]
        public BlendStrategy BlendStrategy { get; set; }

        [Option(HelpText = "Shrinkwrap mesh grid resolution", Default = TexturingDefaults.BLEND_SHRINKWRAP_GRID_RESOLUTION)]
        public int GridResolution { get; set; }

        [Option(HelpText = "Shrinkwrap mesh projection axis (X, Y, Z)", Default = TexturingDefaults.BLEND_SHRINKWRAP_AXIS)]
        public VertexProjection.ProjectionAxis ProjectionAxis { get; set; }

        [Option(HelpText = "Shrinkwrap mode (Project, NearestPoint)", Default = TexturingDefaults.BLEND_SHRINKWRAP_MODE)]
        public Shrinkwrap.ShrinkwrapMode ShrinkwrapMode { get; set; }

        [Option(HelpText = "Shrinkwrap Project miss behaviour (None, Delaunay, Inpaint)", Default = TexturingDefaults.BLEND_SHRINKWRAP_MISS_RESPONSE)]
        public Shrinkwrap.ProjectionMissResponse ShrinkwrapMiss { get; set; }

        [Option(HelpText = "Preadjust image luminance towards global median before blending, 0 to disable, 1 for max", Default = TexturingDefaults.BLEND_PREADJUST_LUMINANCE)]
        public double PreadjustLuminance { get; set; }

        [Option(HelpText = "Redo shrinkwrap mesh", Default = false)]
        public bool RedoShrinkwrapMesh { get; set; }

        [Option(HelpText = "Redo blurred texture", Default = false)]
        public bool RedoBlurredTexture { get; set; }

        [Option(HelpText = "Redo blended texture", Default = false)]
        public bool RedoBlendedTexture { get; set; }
    }

    public class BlendImages : TextureCommand
    {
        private const string OUT_DIR = "texturing/BlendProducts";

        private const double WARP_SHRINKWRAP_UV_THRESHOLD = 1.1;

        private BlendImagesOptions options;

        private bool useExistingLeaves, useExistingIndex;

        private Image blurredTexture;
        private Image blendedTexture;

        private Image originalBackprojectIndex;

        public BlendImages(BlendImagesOptions options) : base(options)
        {
            this.options = options;

            if (options.Redo)
            {
                options.RedoShrinkwrapMesh = true;
                options.RedoBlurredTexture = true;
                options.RedoBlendedTexture = true;
            }

            options.RedoBlurredTexture |= options.RedoBlurredObservationTextures;
            options.RedoBlurredTexture |= options.RedoShrinkwrapMesh;

            options.RedoBlendedTexture |= options.RedoBlurredTexture;

            options.RedoBlendedObservationTextures |= options.RedoBlendedTexture;
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
                    pipeline.LogWarn("no surface observations available");
                    StopStopwatch();
                    return 0;
                }

                if (!useExistingLeaves)
                {
                    if (sceneMesh.Variant == MeshVariant.Shrinkwrap)
                    {
                        RunPhase("load or generate shrinkwrap mesh", LoadOrBuildShrinkwrapMesh);
                    }
                    else
                    {
                        RunPhase("load input mesh", () => LoadInputMesh(requireUVs: useExistingIndex));
                    }
                }

                RunPhase("check or generate observation image masks", BuildObservationImageMasks);
                if ((options.RedoBlurredTexture || sceneMesh.BlurredTextureGuid == Guid.Empty) &&
                    !useExistingIndex && !useExistingLeaves)
                {
                    RunPhase("check or generate observation frustum hulls", BuildObservationImageHulls);
                }
                if (options.TextureVariant == TextureVariant.Stretched)
                {
                    RunPhase("check or generate stretched observation images", BuildStretchedObservationImages);
                }
                if (options.PreadjustLuminance > 0 || options.Colorize)
                {
                    RunPhase("check or generate observation image stats", BuildObservationImageStats);
                }

                RunPhase("check or genererate blurred observation images", BuildBlurredObservationImages);

                //conserve memory: we will (probably) want the textures later, but we can reload them at that point
                RunPhase("clear LRU image cache", ClearImageCache);

                RunPhase("load or generate backproject index", LoadOrBuildBackprojectIndex);

                RunPhase("load or generate blurred texture", LoadOrBuildBlurredTexture);

                RunPhase("load or generate blended texture", LoadOrBuildBlendedTexture);

                RunPhase("generate blended observation images", BuildBlendedObservationImages);

                if (!options.NoSave)
                {
                    if (useExistingLeaves && !string.IsNullOrEmpty(tileList.ImageExt))
                    {
                        RunPhase("generate blended leaf textures", BuildBlendedLeafTextures);
                    }

                    bool saveMesh = !string.IsNullOrEmpty(options.OutputMesh);

                    if ((useExistingIndex && sceneMesh.Variant == MeshVariant.Default) || saveMesh)
                    {
                        RunPhase("generate blended scene texture", BuildBlendedSceneTexture);
                    }
                    
                    if (saveMesh)
                    {
                        RunPhase("save mesh", () => SaveSceneMesh(options.OutputMesh, TextureVariant.Blended));
                    }
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
            if (!base.ParseArgumentsAndLoadCaches(OUT_DIR))
            {
                return false; //help
            }

            sceneMesh = SceneMesh.Find(pipeline, project.Name, MeshVariant.Default);

            if (!options.NoUseExistingLeaves && sceneMesh != null && sceneMesh.TileListGuid != Guid.Empty)
            {
                useExistingLeaves = true;
                pipeline.LogInfo("using existing leaves");
                LoadTileList();
            }

            if (!useExistingLeaves)
            {
                if (!options.NoUseExistingIndex && sceneMesh != null && sceneMesh.BackprojectIndexGuid != Guid.Empty)
                {
                    useExistingIndex = true;
                    pipeline.LogInfo("reprojecting existing backproject index");
                }
                else
                {
                    sceneMesh = SceneMesh.Find(pipeline, project.Name, MeshVariant.Shrinkwrap);
                    if (sceneMesh == null)
                    {
                        sceneMesh = SceneMesh.Create(pipeline, project, MeshVariant.Shrinkwrap, noSave: options.NoSave);
                    }
                    else if (!options.NoUseExistingIndex && sceneMesh.BackprojectIndexGuid != Guid.Empty)
                    {
                        useExistingIndex = true;
                        pipeline.LogInfo("using existing shrinkwrap mesh backproject index");
                    }
                }
            }

            if (!string.IsNullOrEmpty(options.OutputMesh) && useExistingLeaves && !useExistingIndex)
            {
                throw new Exception("cannot save mesh when using existing leaves but not existing index");
            }

            if (options.BlendStrategy == BlendStrategy.Auto)
            {
                options.BlendStrategy =
                    (useExistingLeaves || useExistingIndex) ? BlendStrategy.Inpaint : BlendStrategy.Barycentric;
            }

            switch (options.BlendStrategy)
            {
                case BlendStrategy.None: break;
                case BlendStrategy.Barycentric:
                {
                    options.BarycentricInterpolateWinners = true;
                    options.InpaintDiff = -1;
                    options.BlurDiff = 7;
                    options.NoFillBlendWithAverageDiff = false;
                    break;
                }
                case BlendStrategy.Inpaint:
                {
                    options.BarycentricInterpolateWinners = false;
                    options.InpaintDiff = -1;
                    options.BlurDiff = 7;
                    options.NoFillBlendWithAverageDiff = false;
                    break;
                }
                default: throw new Exception("unknown blend strategy: " + options.BlendStrategy);
            }

            return true;
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            throw new NotImplementedException();
        }

        protected override void LoadTileList()
        {
            base.LoadTileList();

            if (!tileList.HasIndexImages)
            {
                throw new Exception("tile list missing backproject index images");
            }
        }

        protected override bool AllowNoObservations()
        {
            return true; //we'll abort with warning but zero return code if no surface observations
        }

        private void LoadOrBuildShrinkwrapMesh()
        {
            if (sceneMesh.MeshGuid != Guid.Empty && !options.RedoShrinkwrapMesh)
            {
                pipeline.LogInfo("loading existing shrinkwrap mesh from database");
                mesh = pipeline.GetDataProduct<PlyGZDataProduct>(project, sceneMesh.MeshGuid, noCache: true).Mesh;
            }
            else
            {
                double surfaceExtent = 0;
                Mesh inputMesh = null;
                if (!string.IsNullOrEmpty(options.InputMesh))
                {
                    pipeline.LogInfo("loading input mesh from {0}", options.InputMesh);
                    inputMesh = Mesh.Load(pipeline.GetFileCached(options.InputMesh, "meshes"));
                }
                else
                {
                    SceneMesh dsm = SceneMesh.Find(pipeline, project.Name, MeshVariant.Default);
                    if (dsm != null && dsm.MeshGuid != Guid.Empty)
                    {
                        pipeline.LogInfo("loading scene mesh from database");
                        inputMesh =
                            pipeline.GetDataProduct<PlyGZDataProduct>(project, dsm.MeshGuid, noCache: true).Mesh;
                        surfaceExtent = dsm.SurfaceExtent;
                    }
                    else
                    {
                        throw new Exception("no input mesh specified and no scene mesh in database");
                    }
                }

                if (inputMesh == null || inputMesh.Faces.Count == 0)
                {
                    throw new Exception("failed to load input mesh or input mesh empty");
                }

                var sceneBounds = inputMesh.Bounds();
                var sceneExtent = sceneBounds.Extent();
                double xyExtent = 0.5 * (sceneExtent.X + sceneExtent.Y);
                double res = sceneTextureResolution;

                pipeline.LogInfo("generating shrinkwrap mesh in frame {0} from input mesh with {1} faces" +
                                 ": grid resolution {2}, projection axis {3}, mode {4}, miss behavior {5}",
                                 meshFrame, Fmt.KMG(inputMesh.Faces.Count), options.GridResolution,
                                 options.ProjectionAxis, options.ShrinkwrapMode, options.ShrinkwrapMiss);

                Mesh gridMesh = Shrinkwrap.BuildGrid(inputMesh, options.GridResolution, options.GridResolution,
                                                     options.ProjectionAxis, xyExtent > 1 ? 0.01 : 0);

                mesh = Shrinkwrap.Wrap(gridMesh, inputMesh, options.ShrinkwrapMode, options.ProjectionAxis,
                                       options.ShrinkwrapMiss);

                mesh.SwapUVs(); //see comments in GeometryCommand.HeightmapAtlasMesh()

                pipeline.LogInfo("built shrinkwrap mesh with {0} faces", Fmt.KMG(mesh.Faces.Count));
                
                if (surfaceExtent > 0 && xyExtent > WARP_SHRINKWRAP_UV_THRESHOLD * surfaceExtent &&
                    orbitalTextureMetersPerPixel > 0 && !options.NoTextureWarp)
                {
                    ComputeTextureWarp(xyExtent, surfaceExtent, out double srcSurfaceFrac, out double dstSurfaceFrac);

                    if (dstSurfaceFrac > srcSurfaceFrac)
                    {
                        if (options.WriteDebug)
                        {
                            SaveMesh(mesh, meshFrame + "-prewarp");
                        }

                        pipeline.LogInfo("warping {0:F3}x{0:F3} central UVs to {1:F3}x{1:F3}, ease {2:F3}",
                                         srcSurfaceFrac, dstSurfaceFrac, options.EaseTextureWarp);

                        pipeline.LogInfo("central meters per pixel: {0:F3}", surfaceExtent / (dstSurfaceFrac * res));

                        pipeline.LogInfo("orbital meters per pixel: {0:F3}",
                                         (xyExtent - surfaceExtent) / ((1 - dstSurfaceFrac) * res));

                        var surfaceBounds = BoundingBoxExtensions.CreateXY(srcSurfaceFrac * xyExtent);

                        var src = BoundingBoxExtensions.CreateXY(PointToUV(sceneBounds, surfaceBounds.Min),
                                                                 PointToUV(sceneBounds, surfaceBounds.Max));
                        var dst = BoundingBoxExtensions.CreateXY(0.5 * Vector2.One, dstSurfaceFrac);

                        mesh.WarpUVs(src, dst, options.EaseTextureWarp);
                    }
                }

                if (!options.NoSave && mesh.Faces.Count > 0)
                {
                    pipeline.LogInfo("saving shrinkwrap mesh");
                    sceneMesh.SetBounds(mesh.Bounds());
                    var meshProd = new PlyGZDataProduct(mesh);
                    pipeline.SaveDataProduct(project, meshProd, noCache: true);
                    sceneMesh.MeshGuid = meshProd.Guid;
                    sceneMesh.Save(pipeline);
                }
            }

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("shrinkwrap mesh empty");
            }

            if (!mesh.HasUVs)
            {
                throw new Exception("shrinkwrap mesh needs UVs");
            }

            meshLOD = new List<Mesh>() { mesh };

            if (options.WriteDebug)
            {
                SaveMesh(mesh, meshFrame);
            }
        }

        private void LoadOrBuildBackprojectIndex()
        {
            if (useExistingIndex)
            {
                var indexGuid = sceneMesh.BackprojectIndexGuid;
                backprojectIndex = pipeline.GetDataProduct<TiffDataProduct>(project, indexGuid, noCache: true).Image;
                if (sceneMesh.Variant != MeshVariant.Shrinkwrap)
                {
                    pipeline.LogInfo("reprojecting backproject index");
                    ReprojectBackprojectIndex();
                }
                else
                {
                    pipeline.LogInfo("using shrinkwrap backproject index");
                }
                BuildBackprojectResultsFromIndex();
            }
            else if (useExistingLeaves)
            {
                pipeline.LogInfo("using existing leaf backproject indices");
                BuildBackprojectIndexFromLeaves();
                BuildBackprojectResultsFromIndex();
            }
            else
            {
                pipeline.LogInfo("backprojecting shrinkwrap mesh");
                BuildSceneCaster();
                BuildMeshOperator();
                InitBackprojectStrategy();
                BackprojectObservations();
                BuildBackprojectIndex();
            }
        }

        private void LoadOrBuildBlurredTexture()
        {
            if (!options.RedoBlurredTexture && sceneMesh.BlurredTextureGuid != Guid.Empty)
            {
                pipeline.LogInfo("loading blurred texture from database");
                var texGuid = sceneMesh.BlurredTextureGuid;
                blurredTexture = pipeline.GetDataProduct<PngDataProduct>(project, texGuid, noCache: true).Image;
                if (blurredTexture.Width != sceneTextureResolution || blurredTexture.Height != sceneTextureResolution)
                {
                    throw new Exception(string.Format("existing blurred texture or index not {0}x{0}, " +
                                                      "re-run with --redoblurredtexture", sceneTextureResolution));
                }
                if (options.WriteDebug)
                {
                    SaveBackprojectTextureDebug(blurredTexture, TextureVariant.Blurred);
                }
            }
            else
            {
                //note: BuildBackprojectTexture() will colorize blurredTexture if appropriate
                blurredTexture = BuildBackprojectTexture(TextureVariant.Blurred, null, options.PreadjustLuminance);
                pipeline.LogInfo("created {0}x{0} blurred texture", sceneTextureResolution);
            }
        }

        private void LoadOrBuildBlendedTexture()
        {
            void writeDebug()
            {
                if (options.WriteDebug)
                {
                    string name = meshFrame + "_backprojectTexture_" + TextureVariant.Blended;
                    SaveImage(blendedTexture, name);
                    if (mesh != null)
                    {
                        SaveMesh(mesh, name, name + imageExt);
                    }
                }
            }

            if (sceneMesh.BlendedTextureGuid != Guid.Empty && !options.RedoBlendedTexture)
            {
                pipeline.LogInfo("loading blended texture from database");
                var texGuid = sceneMesh.BlendedTextureGuid;
                blendedTexture = pipeline.GetDataProduct<PngDataProduct>(project, texGuid, noCache: true).Image;
                writeDebug();
                return;
            }

            blendedTexture = BlendImage(blurredTexture);

            if (!options.NoSave)
            {
                var texProd = new PngDataProduct(blendedTexture);
                pipeline.SaveDataProduct(project, texProd, noCache: true);
                sceneMesh.BlendedTextureGuid = texProd.Guid;
                sceneMesh.Save(pipeline);
            }

            writeDebug();
        }

        private void BuildBlendedObservationImages()
        {
            BuildBlendedObservationImages(blendedTexture, preadjustLuminance: options.PreadjustLuminance);
        }
        
        private Func<Vector2, Vector2> CreatePixelWarpFunction()
        {
            if (sceneMesh.SurfaceExtent > 0 && orbitalTextureMetersPerPixel > 0 && !options.NoTextureWarp)
            {
                double res = sceneTextureResolution;

                var boundsSize = sceneMesh.GetBounds().Value.Extent();

                double xyExtent = 0.5 * (boundsSize.X + boundsSize.Y);

                ComputeTextureWarp(xyExtent, sceneMesh.SurfaceExtent,
                                   out double srcSurfaceFrac, out double dstSurfaceFrac);

                int srcSurfacePixels = (int)Math.Ceiling(res * srcSurfaceFrac);
                int dstSurfacePixels = (int)Math.Ceiling(res * dstSurfaceFrac);

                if (dstSurfacePixels > srcSurfacePixels)
                {
                    var textureBounds = BoundingBoxExtensions.CreateXY(Vector2.Zero, res * Vector2.One);

                    pipeline.LogInfo("warping {0}x{0} central texture sub-image to {1}x{1}, " +
                                     "total size {2}x{2}, ease {3:F3}",
                                     srcSurfacePixels, dstSurfacePixels, res, options.EaseTextureWarp);

                    pipeline.LogInfo("central meters per pixel: {0:F3}",
                                     sceneMesh.SurfaceExtent / dstSurfacePixels);

                    pipeline.LogInfo("orbital meters per pixel: {0:F3}",
                                     (xyExtent - sceneMesh.SurfaceExtent) / (res - dstSurfacePixels));

                    var src = BoundingBoxExtensions.CreateXY(textureBounds.Center().XY(), srcSurfacePixels);
                    var dst = BoundingBoxExtensions.CreateXY(textureBounds.Center().XY(), dstSurfacePixels);
                    return textureBounds.Create2DWarpFunction(src, dst, options.EaseTextureWarp);
                }
            }
            return null;
        }

        private void BuildBackprojectIndexFromLeaves()
        {
            pipeline.LogInfo("building backproject index from leaves");

            BoundingBox? bounds = null;
            if (!string.IsNullOrEmpty(options.InputMesh))
            {
                pipeline.LogInfo("loading input mesh from {0}", options.InputMesh);
                bounds = Mesh.Load(pipeline.GetFileCached(options.InputMesh, "meshes")).Bounds();
            }
            else
            {
                bounds = sceneMesh.GetBounds();
                if (!bounds.HasValue)
                {
                    throw new Exception(string.Format("scene mesh missing bounds"));
                }
            }
            var boundsSize = bounds.Value.Extent();
            pipeline.LogInfo("scene mesh XY plane bounds: {0:F3}x{1:F3}", boundsSize.X, boundsSize.Y);

            backprojectIndex = new Image(3, sceneTextureResolution, sceneTextureResolution);

            var opts = Rasterizer.Options.DirectToImage(backprojectIndex);

            double maxDim = Math.Max(boundsSize.X, boundsSize.Y);
            opts.MetersPerPixel = maxDim / sceneTextureResolution;

            opts.CameraLocation = bounds.Value.Center();
            mission.GetOrthonormalGISBasisInLocalLevelFrame(out Vector3 elevation,
                                                            out opts.RightInImage, out opts.DownInImage);

            opts.Warp = CreatePixelWarpFunction();
            pipeline.LogInfo("rasterizing {0}x{0} backproject index from {1} leaves, {2:F5} meters/pixel",
                             sceneTextureResolution, tileList.LeafNames.Count, opts.MetersPerPixel);

            string leafFolder = TilingCommand.TILING_DIR;
            CoreLimitedParallel.ForEach(tileList.LeafNames, leaf =>
            {
                string meshUrl = pipeline.GetStorageUrl(leafFolder, project.Name, leaf + tileList.MeshExt);
                var leafMesh = Mesh.Load(pipeline.GetFileCached(meshUrl, "meshes"));

                string indexName = leaf + TilingDefaults.INDEX_FILE_SUFFIX + TilingDefaults.INDEX_FILE_EXT;
                string indexUrl = pipeline.GetStorageUrl(leafFolder, project.Name, indexName);
                var leafIndex = pipeline.LoadImage(indexUrl); //LRU cache for later use in BuildBlendedLeafTextures()

                //only rasterize winning pixels from the leaf
                //any pixels in backprojectIndex that didn't get rasterized then remain masked
                //it seems common that there are some losing pixels right around the leaf boundary
                //(not sure why, mb that is a bug)
                //if we leave those masked then below we can inpaint them
                MaskBackprojectIndex(leafIndex);

                Rasterizer.Rasterize(leafMesh, leafIndex, opts);
            });

            //fill small gaps, particularly along tile boundaries, should make LimberDMG happier
            backprojectIndex.Inpaint(2, useAnyNeighbor: true);

            if (tcopts.WriteDebug)
            {
                SaveBackprojectIndexDebug(backprojectIndex);
            }
        }

        private void ReprojectBackprojectIndex()
        {
            pipeline.LogInfo("reprojecting backproject index top-down");

            var bounds = mesh.Bounds();
            var boundsSize = bounds.Extent();
            pipeline.LogInfo("scene mesh XY plane bounds: {0:F3}x{1:F3}", boundsSize.X, boundsSize.Y);

            var reprojectedBackprojectIndex = new Image(3, sceneTextureResolution, sceneTextureResolution);

            var opts = Rasterizer.Options.DirectToImage(reprojectedBackprojectIndex);

            double maxDim = Math.Max(boundsSize.X, boundsSize.Y);
            opts.MetersPerPixel = maxDim / sceneTextureResolution;

            opts.CameraLocation = bounds.Center();
            mission.GetOrthonormalGISBasisInLocalLevelFrame(out Vector3 elevation,
                                                            out opts.RightInImage, out opts.DownInImage);
            
            opts.Warp = CreatePixelWarpFunction();

            pipeline.LogInfo("reprojecting {0}x{1} backproject index to {2}x{2}, {3:F5} meters/pixel",
                             backprojectIndex.Width, backprojectIndex.Height, sceneTextureResolution,
                             opts.MetersPerPixel);

            MaskBackprojectIndex();

            Rasterizer.Rasterize(mesh, backprojectIndex, opts);

            originalBackprojectIndex = backprojectIndex;
            backprojectIndex = reprojectedBackprojectIndex;

            //fill small gaps, should make LimberDMG happier
            backprojectIndex.Inpaint(2, useAnyNeighbor: true);

            if (tcopts.WriteDebug)
            {
                SaveBackprojectIndexDebug(originalBackprojectIndex, withMesh: false, suffix: "_orig");
                SaveBackprojectIndexDebug(backprojectIndex, withMesh: false);
            }
        }

        private void BuildBlendedLeafTextures()
        {
            BuildBlendedLeafTextures(TilingCommand.TILING_DIR);
        }

        private void BuildBlendedSceneTexture()
        {
            //careful here, in blend-after-texture and blend-after-tiling
            //workflows the sceneMesh is the default scene mesh, not shrinkwrap
            //its backproject index guid and original texture guid, if any, are the atlassed versions matching mesh UVs
            //but its burred and blended texture guids are the BEV reprojected versions
            if (originalBackprojectIndex != null)
            {
                backprojectIndex = originalBackprojectIndex;
                BuildBackprojectResultsFromIndex();
            }
            var dst = sceneMesh.StretchedTextureGuid != Guid.Empty ? TextureVariant.Stretched : TextureVariant.Original;
            sceneTexture = BuildBackprojectTexture(srcTextureVariant: TextureVariant.Blended, dstTextureVariant: dst);
        }
    }
}
