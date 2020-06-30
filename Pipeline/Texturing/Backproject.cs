//#define NO_PARALLEL_RAYCASTS

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;
using OPS.RayTrace;
using OPS.Pipeline.Texturing;

namespace OPS.Pipeline
{
    public class Backproject
    {
        public const int MAX_SAMPLES_PER_BATCH = 500000;
        public const float RAYCAST_NEAR_METERS = 0.001f;
        public static readonly float[] NO_OBSERVATION_COLOR = new float[] { 0.5f, 0.5f, 0.5f };

        public class ObsPixel
        {
            public Observation Obs; //may be (a) RoverObservation, (b) orbital image, (c) null (no observation)
            public Vector2 Pixel; //col, row of pixel in Obs

            public ObsPixel(Observation obs, Vector2 pixel)
            {
                Obs = obs;
                Pixel = pixel;
            }

            public ObsPixel() { }
        }

        public class Context
        {
            public Observation Obs; //observation to backproject

            public Observation MaskObs; //mission rover mask obs corresponding to Obs if any

            public ConvexHull FrustumHull; //frustum hull for observation in mesh space

            public Matrix ObsToMesh;
            public Matrix MeshToObs; 

            public CameraModel CameraModel { get { return Obs.CameraModel; } }

            public Context(Observation obs, Observation maskObs, ConvexHull frustumHull, Matrix obsToMesh)
            {
                Obs = obs;
                MaskObs = maskObs;
                FrustumHull = frustumHull;
                ObsToMesh = obsToMesh;
                MeshToObs = Matrix.Invert(ObsToMesh);
            }
        }

        /// <summary>
        /// high level function that takes backproject results
        /// and emits an image with observations indices and source pixel locations as the pixel colors
        /// output band 0: observation index (see range and special values in Observation.cs)
        /// output band 1: observation pixel row
        /// output band 2: observation column
        /// </summary>
        static public void FillIndexImage(IDictionary<Pixel, ObsPixel> backprojectResults, Image outputImage,
                                          bool clear = true)
        {
            if (backprojectResults == null || backprojectResults.Count == 0)
            {
                return;
            }
            
            if (outputImage.Bands != 3)
            {
                throw new InvalidDataException("Expecting a 3 channel output image for backproject index image");
            }

            if (clear)
            {
                outputImage.Fill(new float[] { Observation.GUTTER_INDEX, 0, 0 });
            }

            CoreLimitedParallel.ForEach(backprojectResults, entry =>
            {
                var outputPixel = entry.Key;
                var obs = entry.Value.Obs;
                var sourceImageIndex = obs != null ? obs.Index : Observation.NO_OBSERVATION_INDEX;
                var sourcePixel = entry.Value.Pixel;

                if (outputPixel.Col < 0 || outputPixel.Col >= outputImage.Width ||
                    outputPixel.Row < 0 || outputPixel.Row >= outputImage.Height)
                {
                    throw new ArgumentException($"backproject output pixel ({outputPixel.Col}, {outputPixel.Row}) " +
                                                $"outside {outputImage.Width}x{outputImage.Height} output image");
                }

                outputImage.SetBandValues(outputPixel.Row, outputPixel.Col,
                                          new float[] { sourceImageIndex, (float)sourcePixel.Y, (float)sourcePixel.X });
            });
        }

        public static Image GenerateIndexPreviewImage(Image index)
        {
            Image previewImg = new Image(3, index.Width, index.Height);
            var colorsByIndex = new ConcurrentDictionary<int, float[]>();
            colorsByIndex[Observation.GUTTER_INDEX] = new float[] { 1, 0, 0 }; //red
            colorsByIndex[Observation.NO_OBSERVATION_INDEX] = new float[] { 1, 1, 0 }; //yellow
            var rnd = NumberHelper.MakeRandomGenerator();
            int numPixels = index.Width * index.Height;
            CoreLimitedParallel.For(0, numPixels, i =>
            {
                previewImg.SetBandValues(i, colorsByIndex.GetOrAdd((int)index.GetBandValues(i)[0], _ =>
                                                                   new float[] { (float)(0.5 * rnd.NextDouble()),
                                                                                 (float)rnd.NextDouble(),
                                                                                 (float)rnd.NextDouble() }));
            });
            return previewImg;
        }
   
        /// <summary>
        /// reconstitute backproject results from an index image
        /// </summary>
        public static IDictionary<Pixel, Backproject.ObsPixel>
            BuildResultsFromIndex(Image index, IDictionary<int, Observation> indexedObservations,
                                  Action<string> warn = null)
        {
            warn = warn ?? (msg => {});
            var bad = new ConcurrentDictionary<int, bool>();
            var results = new ConcurrentDictionary<Pixel, Backproject.ObsPixel>();
            int numPixels = index.Width * index.Height;
            CoreLimitedParallel.For(0, numPixels, i =>
            {
                float[] indexRowCol = index.GetBandValues(i);
                int idx = (int)indexRowCol[0];
                int r = i / index.Width;
                int c = i % index.Width;
                if (idx == Observation.NO_OBSERVATION_INDEX)
                {
                    results[new Pixel(r, c)] = new ObsPixel();
                }
                else if (indexedObservations.ContainsKey(idx))
                {
                    results[new Pixel(r, c)] = new ObsPixel(indexedObservations[idx],
                                                            new Vector2(indexRowCol[2], indexRowCol[1]));
                }
                else if (idx >= Observation.MIN_INDEX && !bad.ContainsKey(idx))
                {
                    bad[idx] = true;
                    warn($"no observation with index {idx}");
                }
            });
            return results;
        }

        /// <summary>
        /// high level function that uses backproject results to populate an image
        /// with corresponding pixels from all the source images
        /// if inpaintMissing is nonzero then missing pixel and gutter areas will be inpainted by that amount
        /// if inpaintGutter is nonzero then gutter areas will be further inpainted by that amount
        /// negative means unlimited inpaint
        /// if outputImage does not have a mask it will be created
        /// if outputImage does have a mask it will be overwritten
        /// project can be null iff textureVariant = TextureVariant.Original or fallbackToOriginal = true
        /// </summary>
        static public Stats FillOutputTexture(PipelineCore pipeline, Project project,
                                              IDictionary<Pixel, ObsPixel> backprojectResults,
                                              Image outputImage, TextureVariant textureVariant,
                                              int inpaintMissing = 4, int inpaintGutter = -1,
                                              bool fallbackToOriginal = true, Image orbitalTexture = null,
                                              float[] missingColor = null)
        {
            missingColor = missingColor ?? NO_OBSERVATION_COLOR;

            var stats = new Stats();

            if (outputImage.Bands != 3)
            {
                throw new NotImplementedException("3 band output image required");
            }

            //set all output pixels invalid
            if (!outputImage.HasMask)
            {
                outputImage.CreateMask(true);
            }
            else
            {
                outputImage.FillMask(true);
            }

            if (backprojectResults == null || backprojectResults.Count == 0)
            {
                outputImage.Fill(missingColor);
                return stats;
            }

            Image getObservationImage(Observation obs)
            {
                if (obs.IsOrbitalImage)
                {
                    return orbitalTexture;
                }

                var variant = fallbackToOriginal ? obs.GetTextureVariantWithFallback(textureVariant) : textureVariant;

                if (project == null && fallbackToOriginal)
                {
                    variant = TextureVariant.Original;
                }

                if (variant == TextureVariant.Original)
                {
                    if (variant != textureVariant)
                    {
                        Interlocked.Increment(ref stats.NumFallbacks);
                    }
                    return pipeline.LoadImage(obs.Url);
                }

                if (project == null)
                {
                    throw new ArgumentException($"project required to load {variant} texture");
                }

                if (project.Name != obs.ProjectName)
                {
                    throw new ArgumentException("cannot load observations from multiple projects: " +
                                                $"{project.Name}, {obs.ProjectName}");
                }

                return pipeline.GetDataProduct<PngDataProduct>(project, obs.GetTextureVariantGuid(variant)).Image;
            }

            var results = backprojectResults.ToList();

            //group by source texture for perfomance (load the image once for all pixels needed from it)
            var groupedWinners =
                results
                .Where(pair => pair.Value.Obs != null &&
                       (pair.Value.Obs is RoverObservation ||
                        (pair.Value.Obs.IsOrbitalImage && orbitalTexture != null)))
                .ToList()
                .GroupBy(pair => pair.Value.Obs.Name);

            var failedObservations = new ConcurrentDictionary<string, IEnumerable<KeyValuePair<Pixel, ObsPixel>>>();
            var failedPixels = new ConcurrentBag<Pixel>();
            CoreLimitedParallel.ForEach(groupedWinners, group =>
            {
                var obs = group.First().Value.Obs;
                Image srcImg = null;
                try
                {
                    srcImg = getObservationImage(obs);
                }
                catch (Exception ex)
                {
                    pipeline.LogWarn("error loading image for observation {0}: {1}", obs.Name, ex.Message);
                }

                if (srcImg == null)
                {
                    failedObservations[obs.Name] = group;
                    return;
                }

                CoreLimitedParallel.ForEach(group, pair =>
                {
                    var dstPx = pair.Key;
                    if (dstPx.Col < 0 || dstPx.Col >= outputImage.Width ||
                        dstPx.Row < 0 || dstPx.Row >= outputImage.Height)
                    {
                        pipeline.LogWarn("backproject output pixel ({0}, {1}) outside {2}x{3} output image",
                                         dstPx.Col, dstPx.Row, outputImage.Width, outputImage.Height);
                        return;
                    }
                    
                    var srcPx = pair.Value.Pixel;
                    if (srcPx.X < 0 || srcPx.X >= srcImg.Width || srcPx.Y < 0 || srcPx.Y >= srcImg.Height)
                    {
                        pipeline.LogWarn("backproject source pixel ({0}, {1}) outside {2}x{3} image " +
                                         "for observation {4}", srcPx.X, srcPx.Y, srcImg.Width, srcImg.Height,
                                         obs.Name);
                        failedPixels.Add(dstPx);
                        return;
                    }

                    var color = srcImg.SampleAsColor(srcPx);
                    outputImage.SetAsColor(color, dstPx.Row, dstPx.Col);
                    outputImage.SetMaskValue(dstPx.Row, dstPx.Col, false);
                    
                    if (obs.IsOrbitalImage)
                    {
                        Interlocked.Increment(ref stats.BackprojectedOrbitalPixels);
                    }
                    else
                    {
                        Interlocked.Increment(ref stats.BackprojectedSurfacePixels);
                    }
                });
            });

            if (failedObservations.Count > 0)
            {
                pipeline.LogWarn("error loading {0} observation images", failedObservations.Count);
            }

            var failed = new HashSet<Pixel>();
            failed.UnionWith(failedPixels);
            foreach (var group in failedObservations.Values)
            {
                failed.UnionWith(group.Select(pair => pair.Key));
            }
            if (failed.Count > 0)
            {
                pipeline.LogWarn("error filling {0} backprojected pixels", Fmt.KMG(failed.Count));
            }

            //at this point all successfull backproject pixels have been filled in and unmasked
            //and all remaining pixels remain masked, including:
            //(a) atlas gutter pixels
            //(b) pixels for which there was no backproject observation (BackprojectResult.Obs == null)
            //(c) pixels for which there was a problem (e.g. failed to load source image, bad coordinates)

            if (inpaintMissing != 0)
            {
                //do some inpaint into both failed and gutter regions
                //generally this is limited so that larger areas that were truly occluded or lacking data
                //are not filled in too much, which is both incorrect and looks bad
                outputImage.Inpaint(inpaintMissing, preserveMask: false);
            }

            //fill in all remaining non-gutter failed pixels with the no-observation color
            failed.UnionWith(results.Where(pair => pair.Value.Obs == null).Select(pair => pair.Key));
            CoreLimitedParallel.ForEach(failed, px =>
            {
                if (px.Col < 0 || px.Col >= outputImage.Width || px.Row < 0 || px.Row >= outputImage.Height)
                {
                    pipeline.LogWarn("backproject failed output pixel ({0}, {1}) outside {2}x{3} output image",
                                     px.Col, px.Row, outputImage.Width, outputImage.Height);
                    return;
                }
                outputImage.SetAsColor(missingColor, px.Row, px.Col);
                outputImage.SetMaskValue(px.Row, px.Col, false);
                Interlocked.Increment(ref stats.BackprojectMissingPixels);
            });

            if (inpaintGutter != 0)
            {
                //inpaint the gutter more, possibly completely if inpaintGutter < 0
                //this can be important for proper behavior of texture minification
                outputImage.Inpaint(inpaintGutter, preserveMask: false);
            }

            return stats;
        }

        public class Options
        {
            public PipelineCore pipeline;

            public Project project;
            public MissionSpecific mission;

            public FrameCache frameCache;
            public ObservationCache observationCache; //for rover mask observations

            public IDictionary<string, ConvexHull> obsToHull; //observation name -> hull, computed if null

            public Mesh mesh; //mesh from which to collect sample points to backproject
            public ConvexHull meshHull; //in mesh frame
            public MeshOperator meshOp; //only uv face tree required
            public string meshFrame;

            public SceneCaster meshCaster;     //for ray casting the active mesh, may be same as occlusionScene
            public SceneCaster occlusionScene; //for checking occlusion of backproject rays

            public bool usePriors;
            public bool onlyAligned;

            public bool writeDebug;
            public string localDebugOutputPath;

            public int outputResolution;

            public double quality; //0 < quality <= 1 (best, slowest)
            public ObsSelectionStrategy obsSelectionStrategy;  //the approach used to pick the best source data

            public Vector3 skyDirInMesh; //for checking orbital occlusion
            public Matrix meshToOrbital;

            public string meshName;

            public bool quiet;
        }

        public class Stats
        {
            public int BackprojectedSurfacePixels;
            public int BackprojectedOrbitalPixels;
            public int BackprojectMissingPixels;
            public int NumFallbacks;
        }

        /// <summary>
        /// high level api with database helpers
        /// this is for when you want to just call with all the observations you have and see what lands on the mesh
        /// performs either or both surface and orbital backproject
        /// depending on whether observations contains rover images and/or orbital imge
        /// returned dictionary will contain at most opts.outputResolution^2 entries
        /// it will have no entry for a texel in the atlas gutter (i.e. not in the used UV space of the mesh)
        /// the entry will have a null observation for non-gutter texels that failed to backproject
        /// </summary>
        static public IDictionary<Pixel, ObsPixel>
            BackprojectObservations(Options opts, IEnumerable<Observation> observations, out Stats stats)
        {
            stats = new Stats();

#if NO_PARALLEL_RAYCASTS
            var results = new Dictionary<Pixel, ObsPixel>();
#else
            var results = new ConcurrentDictionary<Pixel, ObsPixel>();
#endif

            string meshMsg = !string.IsNullOrEmpty(opts.meshName) ? $" for mesh {opts.meshName}" : "";
            Action<string> info = msg => { if (!opts.quiet) opts.pipeline.LogInfo(msg + meshMsg); };
            Action<string> verbose = msg => { if (!opts.quiet) opts.pipeline.LogVerbose(msg + meshMsg); };
            Action<string> warn = msg => opts.pipeline.LogWarn(msg + meshMsg);

            var surfaceImages = observations
                .Where(obs => obs is RoverObservation)
                .Cast<RoverObservation>()
                .Where(obs => obs.ObservationType == RoverProductType.Image)
                .ToList();

            var orbitalImage = observations.Where(obs => obs.IsOrbitalImage).FirstOrDefault();

            int resolution = opts.outputResolution;
            info(string.Format("collecting sample points from mesh to {0}x{0} destination texture", resolution));
            List<PixelPoint> samplePoints = opts.meshOp.SampleUVSpace(resolution, resolution);
            int np = samplePoints.Count;
            info(string.Format("collected {0} sample points", Fmt.KMG(np)));

            if (surfaceImages.Count() == 0 && orbitalImage == null)
            {
                warn("no image observations found");
                foreach (var sample in samplePoints)
                {
                    results[SubpixelToPixel(sample.Pixel)] = new ObsPixel();
                    return results;
                }
            }

            //find the reduced set of observations that intersect the desired mesh
            var intersectingObservations = surfaceImages;
            if (opts.meshHull != null)
            {
                info($"testing {surfaceImages.Count} surface image observations for intersection with mesh hull");
                intersectingObservations = new List<RoverObservation>();
                CoreLimitedParallel.ForEach(surfaceImages, obs =>
                {
                    if (!opts.obsToHull.ContainsKey(obs.Name))
                    {
                        warn($"missing hull for observation {obs.Name}");
                        return;
                    }
                    if (opts.meshHull.Intersects(opts.obsToHull[obs.Name]))
                    {
                        lock (intersectingObservations)
                        {
                            intersectingObservations.Add(obs);
                        }
                        if (opts.writeDebug)
                        {
                            opts.obsToHull[obs.Name].Mesh
                            .Save(PathHelper.EnsureDir(opts.localDebugOutputPath, obs.Name + "_intersectingHull.ply"));
                        }
                    }
                });
            }
            else
            {
                warn("no mesh hull supplied, proceeding with all mesh observations");
            }

            info(string.Format("{0}/{1} image observations intersect mesh",
                               intersectingObservations.Count, surfaceImages.Count));

            if (intersectingObservations.Count() == 0 && orbitalImage == null)
            {
                warn("no intersecting observations found");
                foreach (var sample in samplePoints)
                {
                    results[SubpixelToPixel(sample.Pixel)] = new ObsPixel();
                    return results;
                }
            }

            List<Context> intersectingContexts =
                BuildContexts(opts.obsToHull, intersectingObservations, opts.mission, opts.frameCache,
                              opts.observationCache, opts.meshFrame, opts.usePriors, opts.onlyAligned, warn);

            if (opts.writeDebug)
            {
                info("building debug coverage images");
                //opts.occlusionScene may be built for a full scene mesh
                //of which opts.mesh is only a potententially small subset
                var debugTileOcclusion = new SceneCaster();
                debugTileOcclusion.AddMesh(opts.mesh, null, Matrix.Identity);
                debugTileOcclusion.Build();
                foreach (var ctx in intersectingContexts)
                {
                    DebugWriteCoverageImage(opts, debugTileOcclusion, ctx.Obs, ctx.ObsToMesh);
                }
            }

            var masker = opts.mission.GetMasker();

            var failed = new List<int>();
            int numBatches = (int)Math.Ceiling(((double)samplePoints.Count) / MAX_SAMPLES_PER_BATCH);
            if (intersectingContexts.Count == 0)
            {
                numBatches = 0;
                failed = null; //will be treated as all failed below
            }
            for (int batch = 0; batch < numBatches; batch++)
            {
                int startIdx = batch * MAX_SAMPLES_PER_BATCH;
                int batchSize = Math.Min(samplePoints.Count - startIdx, MAX_SAMPLES_PER_BATCH);

                if (numBatches > 1)
                {
                    info($"backprojecting batch {batch + 1}/{numBatches} of " +
                         $"{Fmt.KMG(batchSize)}/{Fmt.KMG(samplePoints.Count)} pixels");
                }

                verbose($"getting per-pixel {opts.obsSelectionStrategy.Name} sortings " +
                        $"of {intersectingContexts.Count} contexts for {Fmt.KMG(batchSize)} pixels");

                //find the strategy specific ranking of contexts for each pixel in batch
                var sortedContexts = new ConcurrentDictionary<int, List<Context>>();
                CoreLimitedParallel.For(startIdx, startIdx + batchSize, idx =>
                {
                    sortedContexts[idx] =
                    opts.obsSelectionStrategy.FilterAndSortContexts(samplePoints[idx].Point, intersectingContexts,
                                                                    opts.meshCaster);
                });
                int maxContextDepth = sortedContexts.Values.Max(contexts => contexts.Count);
                int minContextDepth = sortedContexts.Values.Min(contexts => contexts.Count);
                
                verbose($"collected from {minContextDepth} to {maxContextDepth} contexts per pixel");
                verbose($"attempting to backproject {Fmt.KMG(batchSize)} pixels");

                var remainingIndices = Enumerable.Range(startIdx, batchSize).ToList();
                for (int contextDepth = 0; remainingIndices.Count > 0 && contextDepth < maxContextDepth; contextDepth++)
                {
                    // remove pixels that had all contexts fail (or which had no contexts)
                    int prevFailed = failed.Count;
                    failed.AddRange(remainingIndices.Where(idx => sortedContexts[idx].Count <= contextDepth));
                    if (failed.Count > prevFailed)
                    {
                        verbose($"gave up on {Fmt.KMG(failed.Count - prevFailed)} pixels with no preference " +
                                $"{contextDepth} observation");
                    }

                    remainingIndices = remainingIndices.Where(idx => sortedContexts[idx].Count > contextDepth).ToList();
                    if (remainingIndices.Count == 0)
                    {
                        break;
                    }
                        
                    //group all remaining points by their current best context
                    var groups = remainingIndices.GroupBy(idx => sortedContexts[idx][contextDepth].Obs.Index);
                    
                    verbose($"attempting to backproject {Fmt.KMG(remainingIndices.Count)} pixels " +
                            $"into {groups.Count()} preference {contextDepth} observations");

                    int no = 0, ns = 0;
                    foreach (var group in groups)
                    {
                        var succeeded = BackprojectSurfaceObs(opts.pipeline, opts.project, opts.occlusionScene, masker,
                                                              sortedContexts[group.First()][contextDepth],
                                                              samplePoints, group, results);
                        if (succeeded.Count > 0)
                        {
                            no++;
                            ns += succeeded.Count;
                            remainingIndices = remainingIndices.Where(idx => !succeeded.Contains(idx)).ToList();
                        }
                    }
                        
                    verbose($"backprojected {Fmt.KMG(ns)} pixels into {no} priority {contextDepth} observations");
                    stats.BackprojectedSurfacePixels += ns;
                }

                failed.AddRange(remainingIndices);
                
                info($"backprojected {Fmt.KMG(batchSize - remainingIndices.Count)} pixels, " +
                     $"{Fmt.KMG(failed.Count)} failed");
            }

            int nf = failed != null ? failed.Count : samplePoints.Count;
            if (nf > 0)
            {
                info($"failed to backproject {Fmt.KMG(nf)} pixels to surface observations");
                if (orbitalImage != null)
                {
                    info("backprojecting into orbital image");
                    var indices = failed ?? Enumerable.Range(0, samplePoints.Count);
                    var succeeded = BackprojectOrbitalObs(orbitalImage, opts.occlusionScene, samplePoints,
                                                          indices, opts.meshToOrbital, opts.skyDirInMesh, results);
                    stats.BackprojectedOrbitalPixels = succeeded.Count;
                    foreach (var idx in indices.Where(idx => !succeeded.Contains(idx)))
                    {
                        results[SubpixelToPixel(samplePoints[idx].Pixel)] = new ObsPixel();
                        stats.BackprojectMissingPixels++;
                    }
                }
                else
                {
                    info("no orbital image");
                    for (int i = 0; i < nf; i++)
                    {
                        results[SubpixelToPixel(samplePoints[failed != null ? failed[i] : i].Pixel)] = new ObsPixel();
                        stats.BackprojectMissingPixels++;
                    }
                }
            }
            
            if (opts.writeDebug)
            {
                var winningObs = results
                    .Where(pair => pair.Value.Obs is RoverObservation)
                    .Select(pair => pair.Value.Obs.Name)
                    .Distinct();
                foreach (var obsName in winningObs)
                {
                    opts.obsToHull[obsName].Mesh
                        .Save(PathHelper.EnsureDir(opts.localDebugOutputPath, obsName + "_winninghull.ply"));
                }
            }

            return results;
        }

        static public IDictionary<Pixel, ObsPixel>
            BackprojectObservations(Options opts, IEnumerable<Observation> observations)
        {
            return BackprojectObservations(opts, observations, out Stats stats);
        }

        public static List<Context> BuildContexts(IDictionary<string, ConvexHull> obsToHull,
                                                  List<RoverObservation> observations, MissionSpecific mission,
                                                  FrameCache frameCache, ObservationCache observationCache,
                                                  string meshFrame, bool usePriors, bool onlyAligned,
                                                  Action<string> warn = null)
        {
            warn = warn ?? (msg => {});
            var contexts = new List<Context>();
            var comparator = mission.GetRoverObservationComparator();
            foreach (var obs in observations)
            {
                var obsToMesh = frameCache.GetObservationTransform(obs, meshFrame, usePriors, onlyAligned);
                if (obsToMesh == null)
                {
                    warn($"failed to get transform for observation {obs.Name}");
                    continue;
                }
                if (!obsToHull.ContainsKey(obs.Name))
                {
                    warn($"no hull for observation {obs.Name}");
                    continue;
                }
                var off = observationCache
                    .GetAllObservationsForFrame(frameCache.GetFrame(obs.FrameName))
                    .Where(o => o is RoverObservation)
                    .ToList();
                var maskObs = comparator
                    .KeepBestRoverObservations(off, RoverObservationComparator.LinearVariants.Both,
                                               RoverProductType.RoverMask)
                    .Where(o => o.IsLinear == obs.IsLinear)
                    .FirstOrDefault();
                contexts.Add(new Context(obs, maskObs, obsToHull[obs.Name], obsToMesh.Mean));
            }
            return contexts;
        }

        private static void DebugWriteCoverageImage(Options opts, SceneCaster debugTileOcclusion, Observation obs,
                                                    Matrix obsToMesh)
        {
            Image srcImg = opts.pipeline.LoadImage(obs.Url);

            Image obsCoverage = new Image(3, obs.Width, obs.Height);
            CameraModel cam = obs.CameraModel;
            Matrix obsToMeshMat = obsToMesh;
            for (int idxRow = 0; idxRow < obs.Height; idxRow++)
            {
                for (int idxCol = 0; idxCol < obs.Width; idxCol++)
                {
                    //intialize with real data
                    obsCoverage.SetAsColor(srcImg.GetBandValues(idxRow, idxCol), idxRow, idxCol);

                    Vector3? ptMesh = RaycastMesh(cam, obsToMeshMat, new Vector2(idxCol, idxRow), debugTileOcclusion);
                    if (ptMesh.HasValue)
                    {
                        Vector3? ptScene = RaycastMesh(cam, obsToMeshMat, new Vector2(idxCol, idxRow),
                                                       opts.occlusionScene);
                        if (ptScene.HasValue)
                        {
                            //check to tell if the points are likely the same
                            if (Vector3.Distance(ptScene.Value, ptMesh.Value) < 0.01)
                            {
                                var bandVals = obsCoverage.GetBandValues(idxRow, idxCol);
                                bandVals[2] += 0.25f; //tint blue
                                obsCoverage.SetBandValues(idxRow, idxCol, bandVals);
                            }
                        }
                        else
                        {
                            // couldn't check tint red
                            var bandVals = obsCoverage.GetBandValues(idxRow, idxCol);
                            bandVals[0] += 0.25f; //tint red
                            obsCoverage.SetBandValues(idxRow, idxCol, bandVals);
                        }
                    }
                }
            }
            obsCoverage.Save<byte>(PathHelper.EnsureDir(opts.localDebugOutputPath, obs.Name + "_coverage.png"));
        }

        //lowest level function that takes a set of PixelPoints to backproject
        //and attempts to backproject them into an observation image context
        //returns set of winners
        private static HashSet<int>
            BackprojectSurfaceObs(PipelineCore pipeline, Project project, SceneCaster occlusionScene,
                                  RoverMasker masker, Context ctx, List<PixelPoint> samplePoints,
                                  IEnumerable<int> indices, IDictionary<Pixel, ObsPixel> results)
        {
            Image mask = ImageMasker.GetOrCreateMask(pipeline, project, ctx.Obs, masker, ctx.MaskObs); //cached

#if NO_PARALLEL_RAYCASTS
            var winners = new HashSet<int>();
            Serial.
#else
            var winners = new ConcurrentBag<int>();
            CoreLimitedParallel.
#endif
            ForEach(indices, index =>
            {
                var pixelPoint = samplePoints[index];

                // validate surface point is in the frustum to avoid camera model issues with offscreen points
                Vector3 meshPos = pixelPoint.Point;
                if (ctx.FrustumHull.Contains(meshPos))
                {
                    //project into observation
                    Vector3 obsPos = Vector3.Transform(meshPos, ctx.MeshToObs);
                    Vector2 obsPixel = ctx.CameraModel.Project(obsPos, out double range);

                    //sanity check: actually needed for CAVHORE where convex hull is overly conservative
                    if (range <= 0 ||
                        obsPixel.X < 0 || obsPixel.X >= ctx.Obs.Width || obsPixel.Y < 0 || obsPixel.Y >= ctx.Obs.Height)
                    {
                        return;
                    }

                    //test if rover masked or missing data
                    //any neighbor pixels that are set to zero will cause the bilinear sample to be less than 1
                    //mask: 0 means bad, 1 means good (opposite of Image.Mask)
                    if (mask == null || mask.BilinearSample(0, (float)obsPixel.Y, (float)obsPixel.X) >= 1)
                    {
                        //raycast the scene to test if the desired position is occluded by terrain
                        if (occlusionScene == null ||
                            !IsOccluded(ctx.CameraModel, obsPixel, meshPos, occlusionScene, range, ctx.ObsToMesh))
                        {
                            results[SubpixelToPixel(pixelPoint.Pixel)] = new ObsPixel(ctx.Obs, obsPixel);
                            winners.Add(index);
                        }
                    }
                }
            });
#if NO_PARALLEL_RAYCASTS
            return winners;
#else
            var tmp = new HashSet<int>();
            tmp.UnionWith(winners);
            return tmp;
#endif
        }

        private static HashSet<int> BackprojectOrbitalObs(Observation orbitalObs, SceneCaster occlusionScene,
                                                          List<PixelPoint> samplePoints, IEnumerable<int> indices,
                                                          Matrix meshToOrbital, Vector3 skyDirInMesh,
                                                          IDictionary<Pixel, ObsPixel> results)
        {
#if NO_PARALLEL_RAYCASTS
            var winners = new HashSet<int>();
            Serial.
#else
            var winners = new ConcurrentBag<int>();
            CoreLimitedParallel.
#endif
            ForEach(indices, index =>
            {
                var sample = samplePoints[index];
                var rayMeshToSky = new Ray(sample.Point, skyDirInMesh);
                if (occlusionScene == null || occlusionScene.RaycastDistance(rayMeshToSky, RAYCAST_NEAR_METERS) == null)
                {
                    var srcPx = orbitalObs.CameraModel.Project(Vector3.Transform(sample.Point, meshToOrbital));
                    if (srcPx.X >= 0 && srcPx.X < orbitalObs.Width && srcPx.Y >= 0 && srcPx.Y < orbitalObs.Height)
                    {
                        results[SubpixelToPixel(sample.Pixel)] = new ObsPixel(orbitalObs, srcPx);
                        winners.Add(index);
                    }
                }
            });
#if NO_PARALLEL_RAYCASTS
            return winners;
#else
            var tmp = new HashSet<int>();
            tmp.UnionWith(winners);
            return tmp;
#endif
        }

        /// <summary>
        /// helper function to test if there is another part of the mesh between the camera and the test point
        /// </summary>
        public static bool IsOccluded(CameraModel camera, Vector2 pixel, Vector3 meshPos, SceneCaster occlusionScene,
                                      double rangeMeshToImage, Matrix obsToMesh)
        {
            Ray rayCamToMesh = GetRayToMesh(camera, obsToMesh, pixel);
            Ray rayMeshToCam = new Ray(meshPos, -rayCamToMesh.Direction);

            //from embree docs:
            //The implementation makes no guarantees that primitives whose hit distance is exactly at
            //(or very close to) tnear or tfar are hit or missed. 
            //If you want to exclude intersections at tnear just pass a slightly enlarged tnear
            double? dist = occlusionScene.RaycastDistance(rayMeshToCam, RAYCAST_NEAR_METERS);

            //if hit something else before camera, occluded
            return (dist != null) && (dist < rangeMeshToImage);
        }

        private static Ray GetRayToMesh(CameraModel camera, Matrix obsToMesh, Vector2 pixel)
        {
            //get ray from camera through pixel associated with meshPos
            Ray rayCamToMeshInObsFrame = camera.Unproject(pixel);

            // convert from observation frame (typically rover_nav) to mesh (output frame, typically "root")
            Ray rayCamToMesh = new Ray(Vector3.Transform(rayCamToMeshInObsFrame.Position, obsToMesh),
                                       Vector3.TransformNormal(rayCamToMeshInObsFrame.Direction, obsToMesh));

            return rayCamToMesh;
        }

        // raycast the mesh with an occulusion check
        public static Vector3? RaycastMesh(CameraModel camera, Matrix obsToMesh, Vector2 pixel,
                                           BoundingBox meshBounds, SceneCaster meshCaster,
                                           SceneCaster occlusionScene, double raycastTolerance = float.Epsilon)
        {            
            //check if pixel ray hit the mesh
            Vector3? meshPos = Backproject.RaycastMesh(camera, obsToMesh, pixel, meshCaster);
            if (!meshPos.HasValue)
            {
                return null;
            }

            //check if occluded by the scene

            //meshBounds may be a subset of the bounds of meshCaster
            //which is one way to ask for raycasts only on a subset of a big mesh
            //fuzzy is important for meshes with degnerate bounds, e.g. all vertices coplanar on an axis aligned plane
            if (!meshBounds.FuzzyContainsPoint(meshPos.Value, 1e-6))
            {
                return null; //ray was occluded by some other part of meshCaster than the part we're interested in
            }

            //another way to ask for raycasts on a subset of a big mesh
            //is to have meshCaster represent a subset of occlusionScene
            //
            //this can also be used to just raycast against one mesh and occlude with a different one entirely
            //e.g. for sky sphere meshCaster is a tile on the skysphere and occlusionScene represents the terrain mesh
            if (occlusionScene != null && occlusionScene != meshCaster)
            {
                Vector3? occluderPos = Backproject.RaycastMesh(camera, obsToMesh, pixel, occlusionScene);
                if (occluderPos.HasValue)
                {
                    double distanceToOccluder = Vector3.DistanceSquared(occluderPos.Value, obsToMesh.Translation);
                    double distanceToMesh = Vector3.DistanceSquared(meshPos.Value, obsToMesh.Translation);
                    if ((distanceToOccluder - distanceToMesh) < raycastTolerance)
                    {
                        return null; //occluded by other geometry
                    }
                }
            }

            return meshPos;
        }

        public static Vector3? RaycastMesh(CameraModel camera, Matrix obsToMesh, Vector2 pixel, SceneCaster sc)
        {
            Ray rayCamToMesh = GetRayToMesh(camera, obsToMesh, pixel);

            //from embree docs:
            //The implementation makes no guarantees that primitives whose hit distance is exactly at
            //(or very close to) tnear or tfar are hit or missed. 
            //If you want to exclude intersections at tnear just pass a slightly enlarged tnear
            return sc.RaycastPosition(rayCamToMesh, RAYCAST_NEAR_METERS);
        }
     
        public static IDictionary<string, ConvexHull> //indexed by observation name
            BuildFrustumHulls(PipelineCore pipeline, FrameCache frameCache, string outputFrame, bool usePriors,
                              bool onlyAligned, IEnumerable<RoverObservation> imgObservations, double farClip = 20)
        {
            int no = imgObservations.Count();

            pipeline.LogInfo("building frustum hulls for {0} observations", no);

            var obsToHull = new ConcurrentDictionary<string, ConvexHull>();

            int nh = 0;
            CoreLimitedParallel.ForEach(imgObservations, obs =>
            {
                Interlocked.Increment(ref nh);
                pipeline.LogDebug("building frustum hull for observation {0}, {1}/{2}", obs.Name, nh, no);
                var meshObs = new WedgeObservations() { Texture = obs };
                var opts = new WedgeObservations.MeshOptions()
                { Frame = outputFrame, UsePriors = usePriors, OnlyAligned = onlyAligned };
                var hull = meshObs.BuildFrustumHull(pipeline, frameCache, opts, uncertaintyInflated: false,
                                                    farClip: farClip);
                if (hull != null)
                {
                    obsToHull.AddOrUpdate(obs.Name, _ => hull, (_, __) => hull);
                }
                else
                {
                    pipeline.LogWarn("failed to build convex hull for observation {0}", obs.Name);
                }
            });

            pipeline.LogInfo("built convex hulls for {0} observations", obsToHull.Count);

            return obsToHull;
        }

        //helper fucntion to convert from subpixel coordinates to integer pixel texture addresses
        private static Pixel SubpixelToPixel(Vector2 subPixel)
        {
            return new Pixel((int)subPixel.Y, (int)subPixel.X);
        }
    }
}
