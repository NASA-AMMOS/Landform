using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using CommandLine;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.RayTrace;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;
using OPS.Pipeline.Texturing;

namespace OPS.Landform
{
    public class TextureCommandOptions : GeometryCommandOptions
    {
        [Option(HelpText = "Option disabled for this command ", Default = 0)]
        public override int DecimateWedgeMeshes { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = 0)]
        public override int DecimateWedgeImages { get; set; }

        [Option(HelpText = "Wedge debug image decimation blocksize, 0 to disable, -1 for auto", Default = -1)]
        public virtual int DecimateDebugWedgeImages { get; set; }

        [Option(Default = null, HelpText = "Scene mesh, search project storage if omitted")]
        public string InputMesh { get; set; }

        [Option(HelpText = "Use level of detail meshes provided in input mesh", Default = false)]
        public bool LoadLODs { get; set; }

        [Option(HelpText = "Occlusion mesh in same frame as input mesh, defaults to input mesh", Default = null)]
        public string OcclusionMesh { get; set; }

        [Option(HelpText = "Observation image texture variant (Original, Blurred, Blended)", Default = TextureVariant.Original)]
        public virtual TextureVariant TextureVariant { get; set; }

        [Option(HelpText = "A tunable parameter for the Observation Selection Strategy used in backproject (range 0-1)", Default = 0.05)]
        public virtual double BackprojectQuality { get; set; }

        [Option(HelpText = "The strategy used to pick which of the many source image candidates for a given area is selected in backproject (Exhaustive, Greedy, Spatial)", Default = ObsSelectionStrategyName.Spatial)]
        public virtual ObsSelectionStrategyName ObsSelectionStrategy { get; set; }
        
        [Option(Required = false, HelpText = "Observation image blur radius", Default = 7)]
        public int ObservationBlurRadius { get; set; }

        [Option(HelpText = "Redo blurred observation textures", Default = false)]
        public bool RedoBlurredObservationTextures { get; set; }

        [Option(HelpText = "Redo observation image masks", Default = false)]
        public bool RedoObservationMasks { get; set; }
    }

    public class TextureCommand : GeometryCommand
    {
        protected TextureCommandOptions tcopts;

        protected IDictionary<string, ConvexHull> obsToHull;

        protected SceneCaster sceneCaster;

        protected ObsSelectionStrategy backprojectStrategy;
        protected IDictionary<Pixel, Backproject.ObsPixel> backprojectResults;
        protected string backprojectDebugDir;
        protected Image backprojectIndex;

        protected TileList tileList;
        protected ObsSelectionStrategy obsSelStrat;
        protected List<Observation> imageObservations;
        protected Dictionary<int, Observation> indexedImages;

        protected SceneMesh sceneMesh; 

        protected List<Mesh> meshLOD; //meshLOD[0] = mesh, coarser LODs populated iff --loadlods
        protected MeshOperator meshOp; //finest LOD
        protected List<MeshOperator> meshOpForLOD; //meshOpForLOD[0] = meshOp, coarser LODs populated iff --loadlods

        protected TextureCommand(TextureCommandOptions tcopts) : base(tcopts)
        {
            this.tcopts = tcopts;
            if (tcopts.Redo)
            {
                tcopts.RedoBlurredObservationTextures = true;
                tcopts.RedoObservationMasks = true;
            }
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            if (tcopts.DecimateWedgeImages < 0 || tcopts.DecimateWedgeImages > 1)
            {
                throw new Exception("--decimatewedgeimages is not implemented for this command");
            }

            if (tcopts.DecimateWedgeMeshes < 0 || tcopts.DecimateWedgeMeshes > 1)
            {
                throw new Exception("--decimatewedgemeshes is not implemented for this command");
            }

            if (!base.ParseArgumentsAndLoadCaches(outDir))
            {
                return false; //help
            }

            obsSelStrat = ObsSelectionStrategy.Create(tcopts.ObsSelectionStrategy);
            backprojectDebugDir = Path.Combine(localOutputPath, "Backproject");

            //some workflows do not load observations, for example tiling an M2020 tactical mesh
            if (observationCache != null)
            {
                imageObservations = observationCache.GetAllObservations()
                    .Where(obs => ((RoverObservation)obs).ObservationType == RoverProductType.Image)
                    .ToList();
                
                //the observation selection strategy has an opportunity to independently define its preference
                //for linear or nonlinear images
                var comparator = new RoverObservationComparator(mission.GetRoverObservationComparator());
                comparator.logger = pipeline.Verbose ? pipeline : null;
                comparator.SetPreferLinearToNonlinear(obsSelStrat.PreferLinearToNonlinear());
                imageObservations = comparator
                    .KeepBestRoverObservations(imageObservations, RoverObservationComparator.LinearVariants.Best,
                                               RoverProductType.Image)
                    .Cast<Observation>()
                    .ToList();
                
                pipeline.LogInfo("{0} image observations", imageObservations.Count);
                
                indexedImages = new Dictionary<int, Observation>();
                foreach (var obs in imageObservations)
                {
                    indexedImages[obs.Index] = obs;
                }
            }

            return true;
        }

        protected override bool ObservationFilter(RoverObservation obs)
        {
            return obs.UseForTexturing && (obs.ObservationType == RoverProductType.Image ||
                                           obs.ObservationType == RoverProductType.RoverMask);
        }

        protected override string DescribeObservationFilter()
        {
            return " texturing images and masks";
        }

        /// <summary>
        /// this override also handles --meshframe=auto
        /// if the project exists and contains only one scene mesh and --meshframe=auto
        /// then that sceneMesh is loaded and meshFrame is set to its name
        /// this allows later commands like local-build-tileset to work without an explicit --meshframe option
        /// and it also handles the case that the scene mesh was specially built, e.g. for only specific observations
        /// </summary>
        protected override Project GetProject()
        {
            var project = base.GetProject(); //throws if project doesn't exist
            meshFrame = tcopts.MeshFrame.ToLower().Trim();
            if (meshFrame == "auto")
            {
                var sceneMeshes = project.GetSceneMeshes();
                if (sceneMeshes.Count() == 1)
                {
                    var sceneMesh = SceneMesh.Load(pipeline, project.Name, sceneMeshes.First());
                    if (sceneMesh.Variant == MeshVariant.Default)
                    {
                        meshFrame = sceneMesh.Frame;
                        this.sceneMesh = sceneMesh;
                        pipeline.LogInfo("only one scene mesh in project {0}: {1}, implied mesh frame {2}",
                                         project.Name, sceneMesh.Name, meshFrame);
                    }
                }
            }
            return project;
        }

        protected override string GetMeshFrame()
        {
            return !string.IsNullOrEmpty(meshFrame) ? meshFrame : tcopts.MeshFrame.ToLower().Trim();
        }

        protected void EnsureOrBuildObservationTextures()
        {
            switch (tcopts.TextureVariant)
            {
                case TextureVariant.Original: break;
                case TextureVariant.Blurred: BuildBlurredObservationImages(); break;
                case TextureVariant.Blended: EnsureBlendedObservationImages(); break;
                default: throw new Exception("unknown texture variant " + tcopts.TextureVariant);
            }
        }

        protected void EnsureBlendedObservationImages()
        {
            foreach (var obs in imageObservations)
            {
                if (obs.BlendedGuid == Guid.Empty)
                {
                    throw new Exception(string.Format("no blended texture for {0}, run blend-images", obs.Name));
                }
            }
        }

        protected void BuildBlurredObservationImages()
        {
            int no = imageObservations.Count;
            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(imageObservations, obs => {

                    if (!tcopts.RedoBlurredObservationTextures && obs.BlurredGuid != Guid.Empty)
                    {
                        Interlocked.Increment(ref nc);
                        return;
                    }

                    Interlocked.Increment(ref np);

                    if (!tcopts.NoProgress)
                    {
                        pipeline.LogInfo("creating blurred image for observation {0}, processing {1} in parallel, " +
                                         "completed {2}/{3}", obs.Name, np, nc, no);
                    }

                    Image orig = pipeline.LoadImage(obs.Url);

                    //notes from TerrainTools PDSImageRoutines.cs
                    //"Used to do a guass blur 4 with photoshop"
                    //the current code is: img.SmoothBlur(13, 13)
                    Image blurredImage = (new Image(orig)).GaussianBoxBlur(tcopts.ObservationBlurRadius);

                    if (tcopts.WriteDebug)
                    {
                        SaveDebugWedgeImage(blurredImage, obs, "_blurred");
                    }

                    if (!tcopts.NoSave)
                    {
                        var imgProd = new PngDataProduct(blurredImage);
                        pipeline.SaveDataProduct(project, imgProd);
                        obs.BlurredGuid = imgProd.Guid;
                        obs.Save(pipeline);
                    }

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });
        }

        protected void BuildObservationImageMasks()
        {
            var comparator =
                mission != null ? mission.GetRoverObservationComparator() : new RoverObservationComparator();
            int no = imageObservations.Count;
            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(imageObservations, obs => {

                    if (!tcopts.RedoObservationMasks && obs.MaskGuid != Guid.Empty)
                    {
                        Interlocked.Increment(ref nc);
                        return;
                    }

                    Interlocked.Increment(ref np);

                    if (!tcopts.NoProgress)
                    {
                        pipeline.LogInfo("creating mask for observation {0}, processing {1} in parallel, " +
                                         "completed {2}/{3}", obs.Name, np, nc, no);
                    }
                    
                    Image img = pipeline.LoadImage(obs.Url);

                    var off = observationCache.GetAllObservationsForFrame(frameCache.GetFrame(obs.FrameName));
                    var maskObs = comparator.KeepBestRoverObservations(off, RoverObservationComparator.LinearVariants.Both, RoverProductType.RoverMask).Where(o => o.IsLinear == obs.IsLinear).FirstOrDefault();

                    Image maskImage = ImageMasker.MakeMask(pipeline, masker, maskObs != null ? maskObs.Url : null, img);

                    if (!tcopts.NoSave)
                    {
                        var maskProd = new PngDataProduct(maskImage);
                        pipeline.SaveDataProduct(project, maskProd);
                        obs.MaskGuid = maskProd.Guid;
                        obs.Save(pipeline);
                    }

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });
        }

        protected void LoadInputMesh(bool requireUVs = true)
        {
            if (sceneMesh == null && project != null) //might have already been loaded in GetProject()
            {
                sceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame);
            }

            if (!string.IsNullOrEmpty(tcopts.InputMesh))
            {
                pipeline.LogInfo("loading input mesh from {0}{1}", tcopts.InputMesh,
                                 sceneMesh != null ? (", overriding scene mesh " + sceneMesh.Name) : "");
                string meshFile = pipeline.GetFileCached(tcopts.InputMesh, "meshes");
                if (tcopts.LoadLODs)
                {
                    meshLOD = Mesh.LoadAllLODs(meshFile);
                }
                else
                {
                    mesh = Mesh.Load(meshFile);
                }
            }
            else if (sceneMesh != null)
            {
                if (sceneMesh.MeshGuid != Guid.Empty)
                {
                    pipeline.LogInfo("loading scene mesh in frame {0} from database", meshFrame);
                    mesh = pipeline.GetDataProduct<PlyGZDataProduct>(project, sceneMesh.MeshGuid).Mesh;
                }
                else
                {
                    throw new Exception("scene mesh in database but without mesh");
                }
            }
            else
            {
                throw new Exception("no input mesh specified and no scene mesh in database");
            }

            if (meshLOD == null)
            {
                meshLOD = new List<Mesh>() { mesh };
            }

            var keepers = new List<Mesh>();
            for (int i = 0; i < meshLOD.Count; i++)
            {
                if (meshLOD[i] == null || meshLOD[i].Faces.Count == 0)
                {
                    pipeline.LogWarn("ignoring empty input mesh at LOD {0}", i);
                }
                else
                {
                    keepers.Add(meshLOD[i]);
                }
            }
            meshLOD = keepers;

            if (meshLOD.Count == 0)
            {
                throw new Exception("failed to load input mesh");
            }

            mesh = meshLOD.First();

            pipeline.LogInfo("input mesh contains {0} non-empty level(s) of detail", meshLOD.Count);
            for (int lod = 0; lod < meshLOD.Count; lod++)
            {
                pipeline.LogInfo("LOD {0}: {1} vertices, {2} faces",
                                 lod, Fmt.KMG(meshLOD[lod].Vertices.Count()), Fmt.KMG(meshLOD[lod].Faces.Count()));
            }

            for (int i = 0; i < meshLOD.Count; i++)
            {
                if (requireUVs && !meshLOD[i].HasUVs)
                {
                    AtlasMesh(meshLOD[i], "lod " + i);
                }
            }
        }

        protected virtual void LoadTileList()
        {
            if (sceneMesh.TileListGuid == Guid.Empty)
            {
                throw new Exception(string.Format("scene mesh {0} has no tile list", sceneMesh.Name));
            }

            tileList = pipeline.GetDataProduct<TileList>(project, sceneMesh.TileListGuid);

            if (tileList.MeshFrame != meshFrame)
            {
                throw new Exception(string.Format("tile list in frame {0}, expected {1}",
                                                  tileList.MeshFrame, meshFrame));
            }

            if (tileList.LeafNames == null || tileList.LeafNames.Count == 0)
            {
                throw new Exception("leaf list empty");
            }
        }

        protected void BuildSceneCaster()
        {
            Mesh occlusionMesh = null;
            if (!string.IsNullOrEmpty(tcopts.OcclusionMesh))
            {
                pipeline.LogInfo("loading occlusion mesh {0}", tcopts.OcclusionMesh);
                occlusionMesh = Mesh.Load(pipeline.GetFileCached(tcopts.OcclusionMesh, "meshes"));
                if (occlusionMesh == null)
                {
                    throw new Exception("failed to load occlusion mesh");
                }
                if (occlusionMesh.Faces.Count == 0)
                {
                    throw new Exception("occlusion mesh empty");
                }
            }
            else
            {
                occlusionMesh = mesh;
            }

            pipeline.LogInfo("building occlusion data structures");
            sceneCaster = new SceneCaster();
            sceneCaster.AddMesh(occlusionMesh, null, Matrix.Identity); //NOTE: can't change mesh after this
            sceneCaster.Build();
        }

        protected void BuildMeshOperator()
        {
            var meshOps = new MeshOperator[meshLOD.Count];
            CoreLimitedParallel.For(0, meshLOD.Count, lod =>
            {
                meshOps[lod] = new MeshOperator(meshLOD[lod],
                                                buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false);
            });
            meshOpForLOD = meshOps.ToList();
            meshOp = meshOpForLOD.First();
        }

        protected void BuildObsHulls()
        {
            obsToHull = Backproject.BuildConvexHulls(pipeline, frameCache, meshFrame, tcopts.UsePriors,
                                                     tcopts.OnlyAligned, imageObservations);
            if (tcopts.WriteDebug)
            {
                foreach (var entry in obsToHull)
                {
                    SaveMesh(entry.Value.Mesh, "Frusta/" + entry.Key);
                }
            }
        }

        protected void InitBackprojectStrategy()
        {
            if (meshOp == null)
            {
                throw new Exception("must build mesh operator before initializing backproject strategy");
            }
            pipeline.LogInfo("initializing backproject observation selection strategy {0} for {1} observations",
                             tcopts.ObsSelectionStrategy, imageObservations.Count);
            backprojectStrategy = ObsSelectionStrategy.Create(tcopts.ObsSelectionStrategy);
            var contexts = Backproject.BuildContexts(obsToHull, imageObservations, mission, frameCache,
                                                     observationCache, meshFrame, tcopts.UsePriors,
                                                     tcopts.OnlyAligned, msg => pipeline.LogWarn(msg));
            backprojectStrategy.Initialize(mesh, meshOp, sceneCaster, contexts, tcopts.BackprojectQuality,
                                           tcopts.WriteDebug, backprojectDebugDir);
        }

        protected void BackprojectObservations()
        {
            pipeline.LogInfo("backprojecting {0} observations", imageObservations.Count);
            BackprojectObservations(mesh, sceneTextureResolution);
        }

        protected IDictionary<Pixel, Backproject.ObsPixel> BackprojectObservations(Mesh mesh, int resolution,
                                                                                   string debugSubdir = "")
        {
            if (backprojectStrategy == null)
            {
                throw new Exception("must initialize backproject strategy before backprojecting observations");
            }
            bool logging = pipeline.Verbose || pipeline.Debug;
            var opts = new Backproject.BackprojectOptions()
            {
                pipeline = pipeline,
                project = project,
                mission = mission,
                frameCache = frameCache,
                observationCache = observationCache,
                observations = imageObservations,
                mesh = mesh,
                meshFrame = meshFrame,
                resolution = resolution,
                sceneOcclusion = sceneCaster,
                usePriors = tcopts.UsePriors,
                onlyAligned = tcopts.OnlyAligned,
                quality = tcopts.BackprojectQuality,
                writeDebug = tcopts.WriteDebug,
                localDebugOutputPath = Path.Combine(backprojectDebugDir, debugSubdir), //ignores empty strings
                obsSelectionStrategy = backprojectStrategy,
                obsToHull = obsToHull,
                info = msg => { if (logging) pipeline.LogInfo(msg); },
                progress = msg => { if (logging && !tcopts.NoProgress) pipeline.LogInfo(msg); },
                warn = msg => pipeline.LogWarn(msg),
                error = msg => pipeline.LogError(msg)
            };
            return Backproject.BackprojectObservations(opts);
        }

        protected void BuildBackprojectIndex()
        {
            pipeline.LogInfo("creating backproject index");
            backprojectIndex = new Image(3, sceneTextureResolution, sceneTextureResolution);
            Backproject.FillIndexImage(backprojectResults, backprojectIndex);

            if (!tcopts.NoSave)
            {
                pipeline.LogInfo("saving backproject index");
                var indexProd = new TiffDataProduct(backprojectIndex);
                pipeline.SaveDataProduct(project, indexProd);
                sceneMesh.BackprojectIndexGuid = indexProd.Guid;
                sceneMesh.Save(pipeline);
            }
            
            if (tcopts.WriteDebug)
            {
                SaveBackprojectIndexDebug(backprojectIndex);
            }
        }

        protected void BuildBackprojectResultsFromIndex()
        {
            pipeline.LogInfo("building backproject results from index");
            backprojectResults = Backproject.BuildResultsFromIndex(backprojectIndex, indexedImages);
        }

        protected Image BuildBackprojectTexture(TextureVariant textureVariant)
        {
            pipeline.LogInfo("creating backproject texture");
            Image texture = new Image(3, sceneTextureResolution, sceneTextureResolution);
            Backproject.FillOutputTexture(pipeline, backprojectResults, texture, textureVariant,
                                          fallbackToOriginal: false);

            if (!tcopts.NoSave)
            {
                pipeline.LogInfo("saving backproject texture");
                var texProd = new PngDataProduct(texture);
                pipeline.SaveDataProduct(project, texProd);
                switch (textureVariant)
                {
                    case TextureVariant.Original: sceneMesh.TextureGuid = texProd.Guid; break;
                    case TextureVariant.Blurred: sceneMesh.BlurredTextureGuid = texProd.Guid; break;
                    case TextureVariant.Blended: sceneMesh.BlendedTextureGuid = texProd.Guid; break;
                    default: throw new Exception("unknown texture variant " + textureVariant);
                }
                sceneMesh.Save(pipeline);
            }
            
            if (tcopts.WriteDebug)
            {
                SaveBackprojectTextureDebug(texture, textureVariant);
            }

            return texture;
        }

        protected void SaveBackprojectIndexDebug(Image index)
        {
            string name = sceneMesh.Name + "_backprojectIndex";
            SaveFloatTIFF(index, name);
            Image previewImg = Backproject.GenerateIndexPreviewImage(index);
            name += "FalseColor";
            pipeline.LogInfo("saving backproject index false color debug image");
            SaveImage(previewImg, name);
            if (mesh != null)
            {
                pipeline.LogInfo("saving backproject index false color textured debug mesh");
                SaveMesh(mesh, name, name + imageExt);
            }
        }

        protected void SaveBackprojectTextureDebug(Image texture, TextureVariant textureVariant)
        {
            string name = sceneMesh.Name + "_backprojectTexture_" + textureVariant.ToString();
            pipeline.LogInfo("saving backproject {0} texture debug image", textureVariant);
            SaveImage(texture, name);
            if (mesh != null)
            {
                pipeline.LogInfo("saving backproject {0} textured debug mesh", textureVariant);
                SaveMesh(mesh, name, name + imageExt);
            }
        }

        protected void SaveDebugWedgeImage(Image img, Observation obs, string suffix)
        {
            int bs = WedgeObservations.AutoDecimate(obs, tcopts.DecimateDebugWedgeImages, tcopts.TargetWedgeImageResolution);
            if (bs > 1)
            {
                img = img.Decimated(bs);
            }
            
            SaveImage(img, obs.Name + suffix);
        }
    }
}
