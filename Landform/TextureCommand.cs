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

        [Option(HelpText = "Occlusion mesh in same frame as input mesh, defaults to input mesh", Default = null)]
        public string OcclusionMesh { get; set; }

        [Option(HelpText = "Output texture resolution, should be power of two", Default = 4096)]
        public virtual int TextureResolution { get; set; }

        [Option(HelpText = "Observation image texture variant (Original, Blurred, Blended)", Default = TextureVariant.Original)]
        public virtual TextureVariant TextureVariant { get; set; }

        [Option(HelpText = "Percentage of pixels to test before picking a texture during backprojection", Default = 0.1)]
        public virtual double BackprojectGoodnessSamplingPct { get; set; }

        [Option(HelpText = "Backproject batching grid cell size in meters, 0 to disable batching", Default = 0)]
        public virtual double BackprojectBatchGridSize { get; set; }

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

        protected int resolution;
        protected IDictionary<string, ConvexHull> obsToHull;
        protected List<Observation> imageObservations;
        protected Dictionary<int, Observation> indexedObservations;
        protected SceneCaster sceneCaster;
        protected IDictionary<Pixel, Backproject.ObsPixel> backprojectResults;
        protected Image backprojectIndex;
        protected TileList tileList;

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

            resolution = tcopts.TextureResolution;
            if (resolution > 0 && (resolution & (resolution - 1)) != 0)
            {
                pipeline.LogWarn("resolution {0} not a power of two", resolution);
            }

            if (observationCache != null)
            {
                var comparator = mission.GetRoverObservationComparator();
                imageObservations = observationCache.GetAllObservations()
                    .Where(obs => obs is RoverObservation)
                    .Where(obs => ((RoverObservation)obs).ObservationType == RoverProductType.Image)
                    .GroupBy(obs => obs.FrameName)
                    .Select(group => group.OrderBy(obs => (RoverObservation)obs, comparator).First())
                    .ToList();
                indexedObservations = new Dictionary<int, Observation>();
                foreach (var obs in imageObservations)
                {
                    indexedObservations[obs.Index] = obs;
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
                    throw new Exception(string.Format("no blended texture for observation {0}, run local-blend-images", obs.Name));
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
            var comparator = mission.GetRoverObservationComparator();
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

                    var maskObs = observationCache.GetAllObservationsForFrame(frameCache.GetFrame(obs.FrameName))
                        .Where(o => o is RoverObservation)
                        .Where(o => ((RoverObservation)o).ObservationType == RoverProductType.RoverMask)
                        .OrderBy(o => (RoverObservation)o, comparator)
                        .FirstOrDefault();

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
            if (sceneMesh == null) //might have already been loaded in GetProject()
            {
                sceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame);
            }

            if (!string.IsNullOrEmpty(tcopts.InputMesh))
            {
                pipeline.LogInfo("loading input mesh from {0}{1}", tcopts.InputMesh,
                                 sceneMesh != null ? (", overriding scene mesh " + sceneMesh.Name) : "");

                if (tcopts.LoadLODs)
                {
                    meshLODs = Mesh.LoadAllLODs(pipeline.GetFileCached(tcopts.InputMesh, "meshes"));
                    
                    if(meshLODs.Count < 2)
                    {
                        throw new Exception("LoadLODs requested, but input mesh has only " + meshLODs.Count + " LODs");
                    }

                    pipeline.LogInfo("Input mesh contains {0} levels of detail", meshLODs.Count);
                    for(int idxLOD = 0; idxLOD < meshLODs.Count; idxLOD++)
                    {
                        Mesh meshLOD = meshLODs.ElementAt(idxLOD);
                        pipeline.LogInfo("Mesh LOD {0}: {1} vertices, {2} faces", idxLOD, meshLOD.Vertices.Count(), meshLOD.Faces.Count());
                    }

                    mesh = meshLODs.First();
                }
                else
                {
                    mesh = Mesh.Load(pipeline.GetFileCached(tcopts.InputMesh, "meshes"));
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

            if (mesh == null)
            {
                throw new Exception("failed to load input mesh");
            }

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("input mesh empty");
            }

            if (requireUVs && !mesh.HasUVs)
            {
                throw new Exception("input mesh needs UVs");
            }

            if (sceneMesh == null)
            {
                sceneMesh = SceneMesh.Create(pipeline, project, meshFrame, MeshVariant.Default, siteDrives, mesh: mesh,
                                             noSave: tcopts.NoSave);
            }
        }

        protected virtual void LoadTileList()
        {
            if (sceneMesh.TileListGuid == Guid.Empty)
            {
                throw new Exception(string.Format("scene mesh {0} has no leaf list, run local-build-leaves",
                                                  sceneMesh.Name));
            }

            tileList = pipeline.GetDataProduct<TileList>(project, sceneMesh.TileListGuid);

            if (tileList.MeshFrame != meshFrame)
            {
                throw new Exception(string.Format("leaf list is in frame {0}, expected {1}",
                                                  tileList.MeshFrame, meshFrame));
            }

            if (tileList.LeafNames == null || tileList.LeafNames.Count == 0)
            {
                throw new Exception("leaf list is empty");
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

        //not using arg default to simplify higher level calls to RunPhase()
        protected void BackprojectObservations()
        {
            BackprojectObservations(logging: true);
        }

        protected void BackprojectObservations(bool logging, bool verbose = false)
        {
            pipeline.LogInfo("backprojecting {0} observations", imageObservations.Count);
            backprojectResults = BackprojectObservations(mesh, logging, verbose);
        }

        protected IDictionary<Pixel, Backproject.ObsPixel>
            BackprojectObservations(Mesh mesh, bool logging, bool verbose = false)
        {
            verbose |= pipeline.Verbose || pipeline.Debug;
            logging |= verbose;
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
                batchGridSize = tcopts.BackprojectBatchGridSize,
                sceneCaster = sceneCaster,
                usePriors = tcopts.UsePriors,
                onlyAligned = tcopts.OnlyAligned,
                quality = tcopts.BackprojectGoodnessSamplingPct,
                obsToHull = obsToHull,
                info = msg => { if (logging) pipeline.LogInfo(msg); },
                progress = msg => { if (verbose && !tcopts.NoProgress) pipeline.LogInfo(msg); },
                warn = msg => pipeline.LogWarn(msg),
                error = msg => pipeline.LogError(msg)
            };
            return Backproject.BackprojectObservations(opts);
        }

        protected void BuildBackprojectIndex()
        {
            pipeline.LogInfo("creating backproject index");
            backprojectIndex = new Image(3, resolution, resolution);
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
            backprojectResults = Backproject.BuildResultsFromIndex(backprojectIndex, indexedObservations);
        }

        protected Image BuildBackprojectTexture(TextureVariant textureVariant)
        {
            pipeline.LogInfo("creating backproject texture");
            Image texture = new Image(3, resolution, resolution);
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
            pipeline.LogInfo("saving backproject index false color textured mesh");
            string name = sceneMesh.Name + "_backprojectIndex";
            SaveFloatTIFF(index, name);
            Image previewImg = Backproject.GenerateIndexPreviewImage(index);
            name += "FalseColor";
            SaveImage(previewImg, name);
            if (mesh != null)
            {
                SaveMesh(mesh, name, name + imageExt);
            }
        }

        protected void SaveBackprojectTextureDebug(Image texture, TextureVariant textureVariant)
        {
            pipeline.LogInfo("saving backproject {0} textured mesh", textureVariant);
            string name = sceneMesh.Name + "_backprojectTexture_" + textureVariant.ToString();
            SaveImage(texture, name);
            if (mesh != null)
            {
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
