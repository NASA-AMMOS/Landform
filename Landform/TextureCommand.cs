//#define DBG_BLURRED
//#define DBG_FRUSTA
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
using OPS.Pipeline.Texturing;
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

        [Option(HelpText = "Use level of detail meshes provided in input mesh", Default = false)]
        public bool LoadLODs { get; set; }

        [Option(HelpText = "Create or fix LOD meshes, comma separated list of min-max ranges, finest to coarsest", Default = null)]
        public string FixupLODs { get; set; }

        [Option(HelpText = "Occlusion mesh in same frame as input mesh, defaults to input mesh", Default = null)]
        public string OcclusionMesh { get; set; }

        [Option(HelpText = "Observation image texture variant (Original, Blurred, Blended)", Default = TextureVariant.Original)]
        public virtual TextureVariant TextureVariant { get; set; }

        [Option(HelpText = "A tunable parameter for the Observation Selection Strategy used in backproject (range 0-1)", Default = 0.3)]
        public virtual double BackprojectQuality { get; set; }

        [Option(HelpText = "The smallest distance (meters) for a raycast determined to be significant, prevents self intersections", Default = 0.0001)]
        public virtual double RaycastTolerance { get; set; }

        [Option(HelpText = "Write extended backproject debug info", Default = false)]
        public bool WriteBackprojectDebug { get; set; }

        [Option(HelpText = "Verbose backproject spew", Default = false)]
        public bool VerboseBackproject { get; set; }

        [Option(HelpText = "The strategy used to pick which of the many source image candidates for a given area is selected in backproject (Exhaustive, Spatial)", Default = ObsSelectionStrategyName.Spatial)]
        public virtual ObsSelectionStrategyName ObsSelectionStrategy { get; set; }
        
        [Option(Required = false, HelpText = "Observation image blur radius", Default = 7)]
        public int ObservationBlurRadius { get; set; }

        [Option(HelpText = "Redo blurred observation textures", Default = false)]
        public bool RedoBlurredObservationTextures { get; set; }

        [Option(HelpText = "Redo observation image masks", Default = false)]
        public bool RedoObservationMasks { get; set; }

        [Option(HelpText = "Redo observation image stats", Default = false)]
        public bool RedoObservationStats { get; set; }

        [Option(HelpText = "Number of inpaint missing pixels for backproject, 0 to disable inpaint, negative for unlimited", Default = 4)]
        public int BackprojectInpaintMissing { get; set; }

        [Option(HelpText = "Number of inpaint gutter pixels for backproject, 0 to disable inpaint, negative for unlimited", Default = -1)]
        public int BackprojectInpaintGutter { get; set; }

        [Option(HelpText = "Just show list of image observations selected for texturing", Default = false)]
        public bool ListImageObservations { get; set; }

        [Option(HelpText = "Length of the convex hull to use when finding observations to texture width (meters)", Default = 100)]
        public virtual double TextureFarClip { get; set; }

        [Option(HelpText = "Prefer color images (Never, Always, EquivalentScores)", Default = PreferColorMode.EquivalentScores)]
        public virtual PreferColorMode PreferColor { get; set; }

        [Option(HelpText = "Colorize mono images to median chrominance", Default = false)]
        public virtual bool Colorize { get; set; }

        [Option(HelpText = "Override median hue [0-360], negative disables (e.g. 33)", Default = -1)]
        public double OverrideMedianHue { get; set; }
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

        protected List<Observation> imageObservations;
        protected List<Observation> orbitalImages;
        protected List<RoverObservation> roverImages;
        protected Dictionary<int, Observation> indexedImages;

        protected SceneMesh sceneMesh;
        protected Image sceneTexture;

        protected Mesh mesh; //finest LOD
        protected List<Mesh> meshLOD; //meshLOD[0] = mesh, coarser LODs populated iff --loadlods
        protected MeshOperator meshOp; //finest LOD
        protected List<MeshOperator> meshOpForLOD; //meshOpForLOD[0] = meshOp, coarser LODs populated iff --loadlods

        protected double medianHue = -1;

        protected TextureCommand(TextureCommandOptions tcopts) : base(tcopts)
        {
            this.tcopts = tcopts;
            if (tcopts.Redo)
            {
                tcopts.RedoBlurredObservationTextures = true;
                tcopts.RedoObservationMasks = true;
                tcopts.RedoObservationStats = true;
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

            if (!tcopts.NoOrbital && !SiteDrive.IsSiteDriveString(meshFrame))
            {
                pipeline.LogInfo("mesh frame \"{0}\" is not a site drive, disabling orbital", meshFrame);
                tcopts.NoOrbital = true;
            }

            backprojectDebugDir = Path.Combine(localOutputPath, "Backproject");

            //some workflows do not load observations, for example tiling an M2020 tactical mesh
            if (observationCache != null)
            {
                orbitalImages = observationCache.GetAllObservations().Where(obs => obs.IsOrbitalImage).ToList();

                roverImages = observationCache.GetAllObservations()
                    .Where(obs => (obs is RoverObservation) &&
                           (((RoverObservation)obs).ObservationType == RoverProductType.Image))
                    .Cast<RoverObservation>()
                    .ToList();

                FilterRoverImages();

                imageObservations = roverImages.Cast<Observation>().ToList();
                imageObservations.AddRange(orbitalImages);
                
                pipeline.LogInfo("{0} image observations ({1} surface, {2} orbital)", imageObservations.Count,
                                 imageObservations.Count - orbitalImages.Count, orbitalImages.Count);

                indexedImages = new Dictionary<int, Observation>();
                foreach (var obs in imageObservations)
                {
                    indexedImages[obs.Index] = obs;
                }

                if (!tcopts.NoOrbital)
                {
                    bool ok = LoadOrbitalTexture();
                    if (!ok && DisableOrbitalIfNoOrbitalTexture())
                    {
                        tcopts.NoOrbital = true;
                    }
                    if (tcopts.NoOrbital && tcopts.NoSurface)
                    {
                        throw new Exception("--nosurface but failed to load orbital");
                    }
                }
            }

            if (tcopts.ListImageObservations)
            {
                ListImageObservations();
                return false;
            }

            if (tcopts.OverrideMedianHue >= 0 && tcopts.OverrideMedianHue <= 360)
            {
                medianHue = tcopts.OverrideMedianHue;
            }

            return true;
        }

        protected virtual bool DisableOrbitalIfNoOrbitalTexture()
        {
            return true;
        }

        protected virtual void FilterRoverImages()
        {
            var comparator = new RoverObservationComparator(mission.GetRoverObservationComparator());
            comparator.logger = pipeline.Verbose ? pipeline : null;
            comparator.SetPreferLinearRasterProducts(mission.PreferLinearRasterProducts());
            roverImages = comparator
                .KeepBestRoverObservations(roverImages, RoverObservationComparator.LinearVariants.Best,
                                           RoverProductType.Image)
                .ToList();
        }

        private void ListImageObservations()
        {
            if (imageObservations != null)
            {
                var allRoverObservations = observationCache.GetAllObservations()
                    .Where(obs => (obs is RoverObservation) &&
                           ((RoverObservation)obs).ObservationType == RoverProductType.Image)
                    .ToList();

                pipeline.LogInfo("{0} surface image observations, {1} linear variants selected for texturing:",
                                 allRoverObservations, roverImages.Count);
                foreach (var obs in allRoverObservations.OrderBy(obs => obs.Name))
                {
                    pipeline.LogInfo("{0} {1}selected for texturing", obs.Name,
                                     indexedImages.ContainsKey(obs.Index) ? "" : "not ");
                }

                pipeline.LogInfo("{0} orbital image observations:", orbitalImages.Count);
                foreach (var obs in orbitalImages.OrderBy(obs => obs.Name))
                {
                    pipeline.LogInfo(obs.Name);
                }
            }
            else
            {
                pipeline.LogInfo("no image observations");
            }
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

        protected void BuildBlurredObservationImages()
        {
            int no = roverImages.Count;
            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(roverImages, obs =>
            {
                if (!tcopts.RedoBlurredObservationTextures && obs.BlurredGuid != Guid.Empty)
                {
#if DBG_BLURRED
                    if (tcopts.WriteDebug)
                    {
                        SaveDebugWedgeImage(pipeline.GetDataProduct<PngDataProduct>(project, obs.BlurredGuid).Image,
                                            obs, "_blurred");
                    }

#endif
                    Interlocked.Increment(ref nc);
                    return;
                }
                
                Interlocked.Increment(ref np);

                pipeline.LogVerbose("creating blurred image for observation {0}, processing {1} in parallel, " +
                                        "completed {2}/{3}", obs.Name, np, nc, no);

                try
                {
                    Image orig = pipeline.LoadImage(obs.Url);
                    
                    //notes from TerrainTools PDSImageRoutines.cs
                    //"Used to do a guass blur 4 with photoshop"
                    //the current code is: img.SmoothBlur(13, 13)
                    Image blurredImage = (new Image(orig)).GaussianBoxBlur(tcopts.ObservationBlurRadius);
                    
#if DBG_BLURRED
                    if (tcopts.WriteDebug)
                    {
                        SaveDebugWedgeImage(blurredImage, obs, "_blurred");
                    }
#endif
                    
                    if (!tcopts.NoSave)
                    {
                        var imgProd = new PngDataProduct(blurredImage);
                        pipeline.SaveDataProduct(project, imgProd);
                        obs.BlurredGuid = imgProd.Guid;
                        obs.Save(pipeline);
                    }
                    
                    Interlocked.Increment(ref nc);
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, $"error creating blurred image for observation {obs.Name}");
                }

                Interlocked.Decrement(ref np);
            });
        }

        protected void BuildObservationImageMasks()
        {
            var comparator =
                mission != null ? mission.GetRoverObservationComparator() : new RoverObservationComparator();
            int no = roverImages.Count;
            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(roverImages, obs =>
            {
                if (!tcopts.RedoObservationMasks && obs.MaskGuid != Guid.Empty)
                {
                    Interlocked.Increment(ref nc);
                    return;
                }
                
                Interlocked.Increment(ref np);
                
                pipeline.LogVerbose("creating mask for observation {0}, processing {1} in parallel, " +
                                    "completed {2}/{3}", obs.Name, np, nc, no);

                try
                {
                    Image img = pipeline.LoadImage(obs.Url);
                    
                    var off = observationCache.GetAllObservationsForFrame(frameCache.GetFrame(obs.FrameName))
                    .Where(o => o is RoverObservation)
                    .ToList();
                    
                    var maskObs = comparator
                    .KeepBestRoverObservations(off, RoverObservationComparator.LinearVariants.Both,
                                               RoverProductType.RoverMask)
                    .Where(o => o.IsLinear == obs.IsLinear)
                    .FirstOrDefault();
                    
                    Image maskImage = ImageMasker.MakeMask(pipeline, masker, maskObs != null ? maskObs.Url : null, img);
                    
                    if (!tcopts.NoSave)
                    {
                        var maskProd = new PngDataProduct(maskImage);
                        pipeline.SaveDataProduct(project, maskProd);
                        obs.MaskGuid = maskProd.Guid;
                        obs.Save(pipeline);
                    }

                    Interlocked.Increment(ref nc);
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, $"error creating mask for observation {obs.Name}");
                }

                Interlocked.Decrement(ref np);
            });
        }

        protected void BuildObservationImageStats()
        {
            int no = roverImages.Count;
            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(roverImages, obs =>
            {
                if (!tcopts.RedoObservationStats && obs.StatsGuid != Guid.Empty)
                {
                    Interlocked.Increment(ref nc);
                    return;
                }
                
                Interlocked.Increment(ref np);
                
                pipeline.LogVerbose("computing stats for observation {0}, processing {1} in parallel, " +
                                    "completed {2}/{3}", obs.Name, np, nc, no);

                try
                {
                    var img = pipeline.LoadImage(obs.Url);
                    if (obs.MaskGuid != Guid.Empty)
                    {
                        var mask = pipeline.GetDataProduct<PngDataProduct>(project, obs.MaskGuid).Image;
                        img = new Image(img); //don't mutate cached image
                        img.UnionMask(mask, new float[] { 0 }); //0 means bad, 1 means good
                    }
                    var statsProd = new ImageStats(img);
                    if (!tcopts.NoSave)
                    {
                        pipeline.SaveDataProduct(project, statsProd);
                        obs.StatsGuid = statsProd.Guid;
                        obs.Save(pipeline);
                    }
                    Interlocked.Increment(ref nc);
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, $"error computing stats for observation {obs.Name}");
                }

                Interlocked.Decrement(ref np);
            });

            int numColor = Backproject.GetImageStats(pipeline, project, roverImages, out double lumaMed,
                                                     out double lumaMAD, out double hueMed);

            if (numColor > 0 && tcopts.OverrideMedianHue < 0)
            {
                medianHue = hueMed;
            }

            pipeline.LogInfo("global luminance median {0:f3}, MAD {1:f3}, hue median {2:f3}, {3}/{4} images color",
                             lumaMed, lumaMAD, hueMed, numColor, roverImages.Count);
        }

        protected void LoadInputMesh(bool requireUVs = true, bool requireNormals = true,
                                     bool onlyGenerateUVsWithTextureProjection = false)
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

            foreach (var lodMesh in meshLOD)
            {
                lodMesh.Clean(verbose: msg => pipeline.LogVerbose(msg), warn: msg => pipeline.LogWarn(msg));
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
            meshLOD = keepers.OrderByDescending(m => m.Faces.Count).ToList();

            if (meshLOD.Count == 0)
            {
                throw new Exception("failed to load input mesh");
            }

            mesh = meshLOD.First();

            pipeline.LogInfo("input mesh contains {0} non-empty level(s) of detail", meshLOD.Count);
            for (int lod = 0; lod < meshLOD.Count; lod++)
            {
                pipeline.LogInfo("LOD {0}: {1} vertices, {2} faces",
                                 lod, Fmt.KMG(meshLOD[lod].Vertices.Count), Fmt.KMG(meshLOD[lod].Faces.Count));
            }

            bool genUVs = !onlyGenerateUVsWithTextureProjection || TextureProjectionEnabled();

            if (tcopts.LoadLODs && !string.IsNullOrEmpty(tcopts.FixupLODs) && (!requireUVs || genUVs))
            {
                int[][] ranges = null;
                try
                {
                    ranges = tcopts.FixupLODs.Split(',')
                        .Select(r => r.Split('-').Select(c => int.Parse(c)).ToArray())
                        .ToArray();
                    if (ranges.Length < 1)
                    {
                        throw new Exception("no triangle ranges");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("error parsing --fixuplods \"" + tcopts.FixupLODs + "\"", ex);
                }

                FixupLODs(ranges);
            }

            for (int i = 0; i < meshLOD.Count; i++)
            {
                if (requireUVs && !meshLOD[i].HasUVs)
                {
                    if (genUVs)
                    {
                        AtlasMesh(meshLOD[i], sceneTextureResolution, "LOD " + i);
                    }
                    else
                    {
                        throw new Exception("atlassing disabled and mesh missing UVs" + (i > 0 ? $" at LOD {i}" : ""));
                    }
                }

                if (requireNormals && !meshLOD[i].HasNormals)
                {
                    meshLOD[i].GenerateVertexNormals();
                }
            }
        }

        protected void FixupLODs(int[][] ranges)
        {
            var newLODs = new Mesh[ranges.Length];
            for (int i = 0; i < ranges.Length; i++)
            {
                int s = meshLOD.FindIndex(m => (ranges[i][0] <= m.Faces.Count && m.Faces.Count <= ranges[i][1]));
                if (s >= 0)
                {
                    Mesh src = meshLOD[s];
                    pipeline.LogInfo("using source LOD {0} with {1} tris for fixed up LOD {2} ({3}-{4})",
                                     s, Fmt.KMG(src.Faces.Count), i, Fmt.KMG(ranges[i][0]), Fmt.KMG(ranges[i][1]));
                    newLODs[i] = src;
                }
                else
                {
                    int target = (int)Math.Round(0.5 * (ranges[i][0] + ranges[i][1]));
                    s = meshLOD.FindLastIndex(m => m.Faces.Count > ranges[i][1]);
                    string st = "source";
                    Mesh src = s >= 0 ? meshLOD[s] : null;
                    if (s < 0 || meshLOD[s].Faces.Count > 2 * target)
                    {
                        int fs = newLODs.ToList().FindLastIndex(m => (m != null && m.Faces.Count >= target));
                        if (fs >= 0)
                        {
                            s = fs;
                            st = "fixed up";
                            src = newLODs[s];
                        }
                    }
                    if (src != null)
                    {
                        newLODs[i] = src.Decimate(target, tcopts.MeshDecimator);
                        pipeline.LogInfo("decimated {0} tri {1} LOD {2} for fixed up LOD {3} ({4}-{5}) " +
                                         "to {6} (target {7}) tris with {8}", Fmt.KMG(src.Faces.Count), st, s, i,
                                         Fmt.KMG(ranges[i][0]), Fmt.KMG(ranges[i][1]),
                                         Fmt.KMG(newLODs[i].Faces.Count), Fmt.KMG(target), tcopts.MeshDecimator);
                    }
                    else
                    {
                        pipeline.LogInfo("no mesh available for making fixed up LOD {0} with {1}-{2} tris",
                                         i, Fmt.KMG(ranges[i][0]), Fmt.KMG(ranges[i][1]));
                    }
                }
            }

            newLODs = newLODs
                .Where(m => m != null && m.Faces.Count > 0)
                .OrderByDescending(m => m.Faces.Count)
                .ToArray();

            if (newLODs.Length > 0)
            {
                meshLOD = newLODs.ToList();
                mesh = meshLOD.First();
            }
            else
            {
                pipeline.LogWarn("LOD fixup failed, using original {0} LODs", meshLOD.Count);
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

            sceneCaster = new SceneCaster(occlusionMesh); //NOTE: can't change mesh after this
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
            obsToHull = Backproject.BuildFrustumHulls(pipeline, frameCache, meshFrame, tcopts.UsePriors,
                                                      tcopts.OnlyAligned, roverImages, farClip: tcopts.TextureFarClip );
#if DBG_FRUSTA
            if (tcopts.WriteDebug)
            {
                foreach (var entry in obsToHull)
                {
                    SaveMesh(entry.Value.Mesh, "Frusta/" + entry.Key);
                }
            }
#endif
        }

        protected virtual void InitBackprojectStrategy()
        {
            if (meshOp == null)
            {
                throw new Exception("must build mesh operator before initializing backproject strategy");
            }
            if (sceneCaster == null)
            {
                throw new Exception("must build scene cater before initializing backproject strategy");
            }
            InitBackprojectStrategy(mesh, meshOp, sceneCaster, sceneCaster);
        }

        protected void InitBackprojectStrategy(Mesh mesh, MeshOperator meshOp, SceneCaster meshCaster,
                                               SceneCaster occlusionScene)
        {
            backprojectStrategy = ObsSelectionStrategy.Create(tcopts.ObsSelectionStrategy);

            backprojectStrategy.Quality = tcopts.BackprojectQuality;
            backprojectStrategy.PreferColor = tcopts.PreferColor;
            backprojectStrategy.RaycastTolerance = tcopts.RaycastTolerance;
            backprojectStrategy.PreferNonlinear = !mission.PreferLinearRasterProducts();
            backprojectStrategy.DebugOutputPath = tcopts.WriteBackprojectDebug ? backprojectDebugDir : null;

            int numOrbital = 0;
            if (!tcopts.NoOrbital && observationCache.ContainsObservation(Observation.ORBITAL_IMAGE_INDEX))
            {
                var texObs = observationCache.GetObservation(Observation.ORBITAL_IMAGE_INDEX);
                backprojectStrategy.OrbitalMetersPerPixel =
                    (texObs.CameraModel as ConformalCameraModel).AvgMetersPerPixel;
                numOrbital = 1;
            }

            pipeline.LogInfo("initializing observation selection strategy {0} for {1} rover observations, {2} orbital",
                             tcopts.ObsSelectionStrategy, roverImages.Count, numOrbital);

            var contexts = Backproject.BuildContexts(obsToHull, roverImages, mission, frameCache,
                                                     observationCache, meshFrame, tcopts.UsePriors,
                                                     tcopts.OnlyAligned, msg => pipeline.LogWarn(msg));

            backprojectStrategy.Initialize(mesh, meshOp, meshCaster, occlusionScene, contexts);
        }

        protected void BackprojectObservations()
        {
            backprojectResults = BackprojectObservations(mesh, sceneTextureResolution, sceneCaster, sceneCaster,
                                                         out Backproject.Stats stats);
        }

        protected IDictionary<Pixel, Backproject.ObsPixel>
            BackprojectObservations(Mesh mesh, int resolution, SceneCaster meshCaster, SceneCaster occlusionScene,
                                    out Backproject.Stats stats, ObsSelectionStrategy strategy = null,
                                    string meshName = "", bool quiet = false)
        {
            string forMesh = !string.IsNullOrEmpty(meshName) ? $" for mesh {meshName}" : "";

            if (mesh.Vertices.Count < 3 || mesh.Faces.Count < 1)
            {
                throw new Exception($"cannot backproject: no triangles{forMesh}");
            }

            strategy = strategy ?? backprojectStrategy;
            if (strategy == null)
            {
                throw new Exception($"must initialize backproject strategy before backprojecting{forMesh}");
            }

            var opts = new Backproject.Options()
            {
                pipeline = pipeline,

                project = project,
                mission = mission,

                frameCache = frameCache,
                observationCache = observationCache,

                obsToHull = obsToHull,

                mesh = mesh,
                meshOp = new MeshOperator(mesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true),
                meshFrame = meshFrame,

                meshCaster = meshCaster,
                occlusionScene = occlusionScene,

                usePriors = tcopts.UsePriors,
                onlyAligned = tcopts.OnlyAligned,

                writeDebug = tcopts.WriteBackprojectDebug,
                localDebugOutputPath = Path.Combine(backprojectDebugDir, meshName), //ignores empty strings

                outputResolution = resolution,

                quality = tcopts.BackprojectQuality,
                obsSelectionStrategy = strategy,

                meshName = meshName,
                quiet = quiet,
                verbose = tcopts.VerboseBackproject
            };

            try
            {
                opts.meshHull = ConvexHull.CreateWithFallback(mesh);
            }
            catch (Exception ex)
            {
                if (!quiet)
                {
                    pipeline.LogWarn("failed to make convex hull{0}: {1}", forMesh, ex.Message);
                }
            }

            if (!tcopts.NoOrbital)
            {
                var meshToRoot = frameCache.GetBestTransform(meshFrame).Transform.Mean;
                opts.meshToOrbital = meshToRoot * Matrix.Invert(orbitalTextureToRoot);
                mission.GetLocalLevelBasis(out Vector3 north, out Vector3 east, out Vector3 nadir);
                opts.skyDirInMesh = -nadir;
            }

            opts = CustomizeBackprojectOptions(opts);

            if (!quiet)
            {
                pipeline.LogInfo("backprojecting {0} observations{1}, resolution {2}, quality {3}, prefer color {4}, " +
                                 "texture far clip {5:f3}",
                                 imageObservations.Count, forMesh, resolution, tcopts.BackprojectQuality,
                                 tcopts.PreferColor, tcopts.TextureFarClip);
            }

            var results = Backproject.BackprojectObservations(opts, imageObservations, out stats);

            if (!quiet)
            {
                pipeline.LogInfo("backprojected {0} pixels from surface{1}, {2} from orbital, {3} failed, " +
                                 "tried up to {4} observations per pixel",
                                 Fmt.KMG(stats.BackprojectedSurfacePixels), forMesh,
                                 Fmt.KMG(stats.BackprojectedOrbitalPixels), Fmt.KMG(stats.BackprojectMissingPixels),
                                 stats.NumFallbacks + 1);
            }

            return results;
        }

        protected virtual Backproject.Options CustomizeBackprojectOptions(Backproject.Options opts)
        {
            return opts;
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

        protected Image MaskBackprojectIndex(Image index)
        {
            index.CreateMask();
            for (int r = 0; r < index.Height; r++)
            {
                for (int c = 0; c < index.Width; c++)
                {
                    if (index[0, r, c] < Observation.MIN_INDEX)
                    {
                        index.SetMaskValue(r, c, true);
                    }
                }
            }
            return index;
        }

        protected void MaskBackprojectIndex()
        {
            MaskBackprojectIndex(backprojectIndex);
        }

        protected void BuildBackprojectResultsFromIndex()
        {
            pipeline.LogInfo("building backproject results from index");
            if (backprojectIndex == null)
            {
                var indexGuid = sceneMesh.BackprojectIndexGuid;
                backprojectIndex = pipeline.GetDataProduct<TiffDataProduct>(project, indexGuid).Image;
            }
            backprojectResults =
                Backproject.BuildResultsFromIndex(backprojectIndex, indexedImages, msg => pipeline.LogWarn(msg));
        }

        protected Image BuildBackprojectTexture(TextureVariant srcTextureVariant,
                                                TextureVariant? dstTextureVariant = null,
                                                double preadjustLuminance = 0)
        {
            //careful here, if we already have a full-scene backprojectIndex
            //then the full-scene texture we're going to generate should be the same resolution
            //in most cases the resolution should match tcopts.TextureResolution
            //but in some workflows, such as blend-after-texture with a lower res blend, it may not
            int width = sceneTextureResolution, height = sceneTextureResolution;
            if (backprojectIndex != null)
            {
                width = backprojectIndex.Width;
                height = backprojectIndex.Height;
            }
            
            pipeline.LogInfo("creating {0}x{1} {2} backproject texture from {3} backproject results, inpaint {4}",
                             width, height, srcTextureVariant, Fmt.KMG(backprojectResults.Count),
                             tcopts.BackprojectInpaintMissing);
            pipeline.LogInfo("preadjust luminance: {0:f3}, colorize: {1}", preadjustLuminance, tcopts.Colorize);

            Image texture = new Image(3, width, height);

            var stats = Backproject.FillOutputTexture(pipeline, project, backprojectResults, texture, srcTextureVariant,
                                                      tcopts.BackprojectInpaintMissing, tcopts.BackprojectInpaintGutter,
                                                      orbitalTexture: orbitalTexture,
                                                      preadjustLuminance: preadjustLuminance,
                                                      colorizeHue: tcopts.Colorize ? medianHue : -1);

            pipeline.LogInfo("filled {0} pixels from {1} surface observations, {2} from orbital, {3} failed, " +
                             "{4} fallbacks to original texture",
                             Fmt.KMG(stats.BackprojectedSurfacePixels), srcTextureVariant,
                             Fmt.KMG(stats.BackprojectedOrbitalPixels), Fmt.KMG(stats.BackprojectMissingPixels),
                             stats.NumFallbacks);

            texture.DumpStats(msg => pipeline.LogInfo(msg));

            if (stats.NumFallbacks > 0)
            {
                pipeline.LogWarn("falling back to {0} texture on {1} observations missing {2} texture",
                                 TextureVariant.Original, stats.NumFallbacks, srcTextureVariant);
            }

            if (!dstTextureVariant.HasValue)
            {
                dstTextureVariant = srcTextureVariant;
            }

            if (!tcopts.NoSave)
            {
                pipeline.LogInfo("saving {0} backproject texture", dstTextureVariant.Value);
                var texProd = new PngDataProduct(texture);
                pipeline.SaveDataProduct(project, texProd);
                switch (dstTextureVariant.Value)
                {
                    case TextureVariant.Original: sceneMesh.TextureGuid = texProd.Guid; break;
                    case TextureVariant.Blurred: sceneMesh.BlurredTextureGuid = texProd.Guid; break;
                    case TextureVariant.Blended: sceneMesh.BlendedTextureGuid = texProd.Guid; break;
                    default: throw new Exception("unknown texture variant " + dstTextureVariant.Value);
                }
                sceneMesh.Save(pipeline);
            }
            
            if (tcopts.WriteDebug)
            {
                SaveBackprojectTextureDebug(texture, dstTextureVariant.Value);
            }

            return texture;
        }

        protected void SaveBackprojectIndexDebug(Image index, bool withMesh = true, string suffix = "")
        {
            string name = sceneMesh.Name + "_backprojectIndex" + suffix;
            SaveFloatTIFF(index, name);
            Image previewImg = Backproject.GenerateIndexPreviewImage(index);
            name = sceneMesh.Name + "_backprojectIndexFalseColor" + suffix;
            pipeline.LogInfo("saving backproject index false color debug image");
            SaveImage(previewImg, name);
            if (withMesh && mesh != null)
            {
                pipeline.LogInfo("saving backproject index false color textured debug mesh");
                SaveMesh(mesh, name, name + imageExt);
            }
        }

        protected void SaveBackprojectTextureDebug(Image texture,
                                                   TextureVariant textureVariant = TextureVariant.Original,
                                                   bool withMesh = true, string suffix = "")
        {
            string name = sceneMesh.Name + "_backprojectTexture";
            if (textureVariant != TextureVariant.Original)
            {
                name += "_" + textureVariant.ToString();
            }
            name += suffix;
            pipeline.LogInfo("saving backproject {0} texture debug image", textureVariant);
            SaveImage(texture, name);
            if (withMesh && mesh != null)
            {
                pipeline.LogInfo("saving backproject {0} textured debug mesh", textureVariant);
                SaveMesh(mesh, name, name + imageExt);
            }
        }

        protected void SaveDebugWedgeImage(Image img, Observation obs, string suffix)
        {
            int bs = WedgeObservations.AutoDecimate(obs, tcopts.DecimateDebugWedgeImages,
                                                    tcopts.TargetWedgeImageResolution);
            if (bs > 1)
            {
                img = img.Decimated(bs);
            }
            
            SaveImage(img, obs.Name + suffix);
        }

        protected void SaveSceneMesh(string outputMesh, bool withIndex = false)
        {
            var meshURL = CheckOutputURL(outputMesh, sceneMesh.Name, outputFolder, MeshSerializers.Instance);
            var imgURL = StringHelper.ChangeUrlExtension(meshURL, imageExt);

            if (withIndex)
            {
                var index = backprojectIndex;
                if (index == null && sceneMesh.BackprojectIndexGuid != Guid.Empty)
                {
                    index = pipeline.GetDataProduct<TiffDataProduct>(project, sceneMesh.BackprojectIndexGuid).Image;
                }
                if (index != null)
                {
                    var ext = ".tif";
                    var indexURL = StringHelper.ChangeUrlExtension(meshURL, ext);
                    pipeline.LogInfo("saving {0}x{1} float tiff backproject index image {2}",
                                     index.Width, index.Height, indexURL);
                    TemporaryFile.GetAndDelete(ext, tmpFile =>
                    {
                        var opts = new GDALTIFFWriteOptions(GDALTIFFWriteOptions.CompressionType.DEFLATE);
                        var serializer = new GDALSerializer(opts);
                        serializer.Write<float>(tmpFile, index);
                        pipeline.SaveFile(tmpFile, indexURL, constrainToStorage: false);
                    });
                }
            }

            var texture = sceneTexture;
            if (texture == null)
            {
                Guid texGuid = Guid.Empty;
                switch (tcopts.TextureVariant)
                {
                    case TextureVariant.Original: texGuid = sceneMesh.TextureGuid; break;
                    case TextureVariant.Blurred: texGuid = sceneMesh.BlurredTextureGuid; break;
                    case TextureVariant.Blended: texGuid = sceneMesh.BlendedTextureGuid; break;
                    default: throw new Exception("unknown texture variant " + tcopts.TextureVariant);
                }
                if (texGuid != Guid.Empty)
                {
                    texture = pipeline.GetDataProduct<PngDataProduct>(project, texGuid).Image;
                }
            }

            if (texture != null)
            {
                pipeline.LogInfo("saving {0}x{1} scene texture {2}", texture.Width, texture.Height, imgURL);
                TemporaryFile.GetAndDelete(imageExt, tmpFile =>
                {
                    texture.Save<byte>(tmpFile);
                    pipeline.SaveFile(tmpFile, imgURL, constrainToStorage: false);
                });
            }

            var mesh = this.mesh;
            if (mesh == null && sceneMesh.MeshGuid != Guid.Empty)
            {
                mesh = pipeline.GetDataProduct<PlyGZDataProduct>(project, sceneMesh.MeshGuid).Mesh;
            }

            if (mesh != null)
            {
                pipeline.LogInfo("saving {0}scene mesh", texture != null ? "textured " : "");
                TemporaryFile.GetAndDelete(StringHelper.GetUrlExtension(meshURL), tmpFile =>
                {
                    string texFile = texture != null ? StringHelper.GetLastUrlPathSegment(imgURL) : null;
                    mesh.Save(tmpFile, texFile);
                    pipeline.SaveFile(tmpFile, meshURL, constrainToStorage: false);
                });
            }
            else
            {
                pipeline.LogWarn("no scene mesh to save");
            }
        }
    }
}
