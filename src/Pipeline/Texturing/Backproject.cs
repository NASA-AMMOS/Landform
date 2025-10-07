//#define NO_PARALLEL_RAYCASTS
#define PARALLELIZE_BATCHES
#define PARALLELIZE_CONTEXTS
#define BACKPROJECT_CHECK_HULL

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.IO;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.MathExtensions;
using JPLOPS.Imaging;
using JPLOPS.Geometry;
using JPLOPS.Pipeline.AlignmentServer;
using JPLOPS.RayTrace;
using JPLOPS.Pipeline.Texturing;

namespace JPLOPS.Pipeline
{
    public class Backproject
    {
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

            public override int GetHashCode()
            {
                return Obs.Name.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                return obj is Context && (obj as Context).Obs.Name == Obs.Name;
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

        static public int GetImageStats(PipelineCore pipeline, Project project, IEnumerable<Observation> observations,
                                        out double luminanceMed, out double luminanceMAD, out double hueMed)
        {
            List<double> lumaMeds = new List<double>(), lumaMADs = new List<double>(), hueMeds = new List<double>();

            foreach (var obs in observations.Where(obs => obs.StatsGuid != Guid.Empty))
            {
                var stats = pipeline.GetDataProduct<ImageStats>(project, obs.StatsGuid, noCache: true);
                lumaMeds.Add(stats.LuminanceMedian);
                lumaMADs.Add(stats.LuminanceMedianAbsoluteDeviation);
                if (stats.Bands > 2)
                {
                    hueMeds.Add(stats.HueMedian);
                }
            }

            luminanceMed = luminanceMAD = -1;
            if (lumaMeds.Count > 0)
            {
                lumaMeds.Sort();
                lumaMADs.Sort();
                luminanceMed = lumaMeds[lumaMeds.Count / 2];
                luminanceMAD = lumaMADs[lumaMADs.Count / 2];
            }

            hueMed = 0;
            if (hueMeds.Count > 0)
            {
                hueMeds.Sort();
                hueMed = hueMeds[hueMeds.Count / 2];
            }

            return hueMeds.Count;
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
                                              float[] missingColor = null, double preadjustLuminance = 0,
                                              double colorizeHue = -1, bool reverseAccessOrder = false,
                                              bool disableCaches = false)
        {
            missingColor = missingColor ?? TexturingDefaults.BACKPROJECT_NO_OBSERVATION_COLOR;

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

            var results = backprojectResults.ToList();

            //group by source texture for perfomance (load the image once for all pixels needed from it)
            //in order of observation name for cache optimization
            var winners =
                results
                .Where(pair => pair.Value.Obs != null &&
                       (pair.Value.Obs is RoverObservation ||
                        (pair.Value.Obs.IsOrbitalImage && orbitalTexture != null)))
                .OrderBy(pair => pair.Value.Obs.Name)
                .ToList();

            if (reverseAccessOrder)
            {
                winners.Reverse();
            }

            var groupedWinners = winners.GroupBy(pair => pair.Value.Obs.Name); //preserves order

            double lumaMed = -1, lumaMAD = -1;
            if (preadjustLuminance > 0)
            {
                GetImageStats(pipeline, project, groupedWinners.Select(group => group.First().Value.Obs),
                              out lumaMed, out lumaMAD, out double hueMed);
            }

            void checkProject(Observation obs)
            {
                if (project == null || project.Name != obs.ProjectName)
                {
                    throw new ArgumentException($"invalid project: {project?.Name} != {obs.ProjectName}");
                }
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

                Image img = null;
                if (variant == TextureVariant.Original)
                {
                    if (variant != textureVariant)
                    {
                        Interlocked.Increment(ref stats.NumFallbacks);
                    }
                    img = pipeline.LoadImage(obs.Url, noCache: disableCaches);
                }
                else
                {
                    checkProject(obs);
                    img = pipeline
                        .GetDataProduct<PngDataProduct>(project, obs.GetTextureVariantGuid(variant),
                                                        noCache: disableCaches)
                        .Image;
                }

                if (preadjustLuminance > 0 && lumaMed >= 0 && obs.StatsGuid != Guid.Empty)
                {
                    checkProject(obs);
                    var st = pipeline.GetDataProduct<ImageStats>(project, obs.StatsGuid, noCache: disableCaches);
                    img = new Image(img); //don't mutate cached image
                    img.AdjustLuminanceDistribution(st.LuminanceMedian, st.LuminanceMedianAbsoluteDeviation,
                                                    lumaMed, lumaMAD, preadjustLuminance);
                }

                return img;
            }

            var failedObservations = new ConcurrentDictionary<string, IEnumerable<KeyValuePair<Pixel, ObsPixel>>>();
            var failedPixels = new ConcurrentBag<Pixel>();
            var isMono = colorizeHue >= 0 ? new ConcurrentDictionary<Pixel, bool>() : null;
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

                    float[] color = srcImg.SampleAsColor(srcPx);
                    outputImage.SetAsColor(color, dstPx.Row, dstPx.Col);
                    outputImage.SetMaskValue(dstPx.Row, dstPx.Col, false);

                    if (isMono != null)
                    {
                        isMono[dstPx] = srcImg.Bands < 3;
                    }
                    
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

            if (colorizeHue >= 0)
            {
                outputImage.ColorizeSelected(colorizeHue, (row, col) => isMono[new Pixel(row, col)],
                                             LuminanceMode.Average);
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

            public Func<List<PixelPoint>, List<PixelPoint>> sampleTransform;  //used e.g. by BuildSkySphere

            public bool onlyCompletelyUnobstructed; //skip pts in frame but occluded in *any* obs (other than orbital)

            public double raycastTolerance;
            public double maxGlancingAngleDegrees = 90; //only respected if occlusionScene == meshCaster != null
            
            public string meshName;

            public bool quiet, verbose;
        }

        public class Stats
        {
            public int BackprojectedSurfacePixels;
            public int BackprojectedOrbitalPixels;
            public int BackprojectMissingPixels;

            //for BackprojectObservations() this is the max used context depth
            //for FillOutputTexture() it's the number of original textures  used instead of the requested variant
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

            var results = new ConcurrentDictionary<Pixel, ObsPixel>();

            string meshMsg = !string.IsNullOrEmpty(opts.meshName) ? $" for mesh {opts.meshName}" : "";
            Action<string> info = msg => { if (!opts.quiet || opts.verbose) opts.pipeline.LogInfo(msg + meshMsg); };
            Action<string> verbose = msg => { if (opts.verbose) opts.pipeline.LogInfo(msg + meshMsg); };
            Action<string> warn = msg => opts.pipeline.LogWarn(msg + meshMsg);

            var surfaceImages = observations
                .Where(obs => obs is RoverObservation)
                .Cast<RoverObservation>()
                .Where(obs => obs.ObservationType == RoverProductType.Image)
                .ToList();

            var orbitalImage = observations.Where(obs => obs.IsOrbitalImage).FirstOrDefault();

            int resolution = opts.outputResolution;
            info($"collecting sample points from mesh to {resolution}x{resolution} destination texture");
            List<PixelPoint> samplePoints = opts.meshOp.SampleUVSpace(resolution, resolution);
            int numPixels = samplePoints.Count;
            info($"collected {Fmt.KMG(numPixels)} sample points");

            if (opts.sampleTransform != null)
            {
                info($"applying custom transform to {Fmt.KMG(numPixels)} sample points");
                samplePoints = opts.sampleTransform(samplePoints);
            }

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

            info($"{intersectingObservations.Count}/{surfaceImages.Count} image observations intersect mesh");

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

            double maxGlancingAngleCosine = 0;
            if (opts.maxGlancingAngleDegrees > 0 && (opts.meshCaster != null) &&
                (opts.meshCaster == opts.occlusionScene))
            {
                maxGlancingAngleCosine = Math.Cos(MathHelper.ToRadians(opts.maxGlancingAngleDegrees));
            }
            info($"max glancing angle {opts.maxGlancingAngleDegrees:F2}deg, cosine {maxGlancingAngleCosine:F2}");

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
            var strategy = opts.obsSelectionStrategy;

            //indices of pixels that we permanently gave up on backprojecting from surface observations
            //if orbital is available we'll try backprojecting these pixels from it below
            var failed = new List<int>(samplePoints.Count);

            int numBatches =
                (int)Math.Ceiling(((double)samplePoints.Count) / TexturingDefaults.BACKPROJECT_MAX_SAMPLES_PER_BATCH);

            if (intersectingContexts.Count == 0) //no usable surface observations
            {
                numBatches = 0;
                failed = null; //will be treated as all failed below
            }

            int backprojectedSurfacePixels = 0;
            int numFallbacks = 0;
            double lastSpew = UTCTime.Now();

#if NO_PARALLEL_RAYCASTS || !PARALLELIZE_BATCHES
            Serial.
#else
            CoreLimitedParallel.
#endif
            For(0, numBatches, batch =>
            {
                int startIdx = batch * TexturingDefaults.BACKPROJECT_MAX_SAMPLES_PER_BATCH;
                int batchSize =
                    Math.Min(samplePoints.Count - startIdx, TexturingDefaults.BACKPROJECT_MAX_SAMPLES_PER_BATCH);

                if (numBatches > 1)
                {
                    info($"backprojecting batch {batch + 1}/{numBatches} of " +
                         $"{Fmt.KMG(batchSize)}/{Fmt.KMG(samplePoints.Count)} pixels");
                }

                verbose($"getting per-pixel {strategy.Name} sortings of {intersectingContexts.Count} contexts " +
                        $"for {Fmt.KMG(batchSize)} pixels");

                //find the strategy specific ranking of observations for each pixel in batch
                //the strategy doesn't actually load the observation images and masks
                //so it can't know if an observation can't be used for a specific pixel because it's masked there
                //we do that below where we can coalesce the image loads for performance
                //trying each observation in order of preference until we find one that can be used
                var sortedContexts = new ConcurrentDictionary<int, List<ObsSelectionStrategy.ScoredContext>>();
                var pt = new PerfTimer();
                CoreLimitedParallel.For(startIdx, startIdx + batchSize, i =>
                {
                    sortedContexts[i] = strategy.FilterAndSortContexts(samplePoints[i].Point, intersectingContexts,
                                                                       opts.meshCaster);
                });
                int maxNumLevels = sortedContexts.Values.Max(contexts => contexts != null ? contexts.Count : 0);
                int minNumLevels = sortedContexts.Values.Min(contexts => contexts != null ? contexts.Count : 0);
                verbose($"collected from {minNumLevels} to {maxNumLevels} contexts per pixel ({pt.HMSR})");

                //indices of pixels that we're still trying to backproject to surface observations in this batch
                var remaining = Enumerable.Range(startIdx, batchSize).ToList();

                // remove pixels which had no contexts
                int numFailed = 0;
                var invis = remaining.Where(i => sortedContexts[i] == null || sortedContexts[i].Count == 0).ToList();
                if (invis.Count > 0)
                {
                    lock (failed)
                    {
                        failed.AddRange(invis);
                    }
                    numFailed += invis.Count;
                    verbose($"{Fmt.KMG(invis.Count)} pixels visible in no surface observation");
                    remaining = remaining.Where(i => sortedContexts[i] != null && sortedContexts[i].Count > 0).ToList();
                }

                if (opts.onlyCompletelyUnobstructed && opts.occlusionScene != null)
                {
                    var contexts = intersectingContexts;
                    var obstructed = remaining
                        .Where(i => contexts.Any(c => IsObstructed(samplePoints[i].Point, opts.occlusionScene, c,
                                                                   opts.raycastTolerance)))
                        .ToList();
                    if (obstructed.Count > 0)
                    {
                        lock (failed)
                        {
                            failed.AddRange(obstructed);
                        }
                        numFailed += obstructed.Count;
                        verbose($"{Fmt.KMG(obstructed.Count)} pixels in frame but occluded in some surface obs");
                        var obs = new HashSet<int>(obstructed);
                        remaining = remaining.Where(i => !obs.Contains(i)).ToList();
                    }
                }
                
                //try to backproject into best scoring observations first
                int maxUsedLevel = 0, numWinners = 0;
                for (int level = 0; remaining.Count > 0 && level < maxNumLevels; level++)
                {
                    verbose($"starting backproject into preference {level} observations");

                    // remove pixels that had all contexts fail (or which had no contexts)
                    var dead = remaining.Where(i => sortedContexts[i].Count <= level).ToList();
                    if (dead.Count > 0)
                    {
                        lock (failed)
                        {
                            failed.AddRange(dead);
                        }
                        numFailed += dead.Count;
                        verbose($"{Fmt.KMG(dead.Count)} pixels with no preference {level} or lower surface obs");
                        remaining = remaining.Where(i => sortedContexts[i].Count > level).ToList();
                    }
                    
                    if (remaining.Count == 0)
                    {
                        break;
                    }

                    //group all remaining points by their current best context
                    var samplesByCtx = remaining
                        .GroupBy(i => sortedContexts[i][level].Context)
                        .ToDictionary(group => group.Key, group => group.ToList());
                    
                    verbose($"attempting to backproject {Fmt.KMG(remaining.Count)} pixels into {samplesByCtx.Count} " +
                            $"preference {level} surface observations");

                    var losers = new List<int>(remaining.Count);
                    int nw = 0, no = 0;

                    //we typically don't parallelize here because in the typical tiling workflow
                    //we are already backprojecting multiple tiles in parallel
                    //and when we're backprojecting a full scene in BuildTexture we're already parallelizing on batches
#if NO_PARALLEL_RAYCASTS || !PARALLELIZE_CONTEXTS
                    Serial.
#else
                    CoreLimitedParallel.
#endif
                    ForEach(samplesByCtx.Keys, ctx =>
                    {
                        var samples = samplesByCtx[ctx];
                        var wl = BackprojectSurfaceObs(opts.pipeline, opts.project, opts.occlusionScene, masker,
                                                       opts.raycastTolerance, maxGlancingAngleCosine,
                                                       ctx, samplePoints, samples, results, opts.verbose);
                        int wc = wl.winners.Count;
                        int lc = wl.losers.Count;
                        if (wc > 0)
                        {
                            Interlocked.Add(ref nw, wc);
                            Interlocked.Add(ref no, 1);
                            Interlocked.Add(ref backprojectedSurfacePixels, wc);
                        }
                        if (lc > 0)
                        {
                            lock (losers)
                            {
                                losers.AddRange(wl.losers);
                            }
                        }
                        if (opts.verbose)
                        {
                            verbose($"backprojected {Fmt.KMG(wc)}/{Fmt.KMG(samples.Count)} pixels " +
                                    $"into preference {level} surface observation {ctx.Obs.Name}, " +
                                    $"{Fmt.KMG(lc)} failed: " + wl.GetStats());
                        }

                        double now = UTCTime.Now();
                        if ((now - lastSpew) > 5)
                        {
                            int fc = 0;
                            lock (failed)
                            {
                                fc = failed.Count;
                            }
                            int bsp = backprojectedSurfacePixels;
                            info($"backprojected {Fmt.KMG(bsp)} pixels, {Fmt.KMG(fc)} failed " +
                                 $"({0.01*(int)((10000.0 * (bsp + fc)) / numPixels)}%)");
                            lastSpew = now;
                        }
                    }); //for each context

                    numWinners += nw;

                    remaining.Clear();
                    lock (losers)
                    {
                        remaining.AddRange(losers);
                    }

                    verbose($"backprojected {Fmt.KMG(nw)} pixels into {no} preference {level} surface observations, " +
                            $"{Fmt.KMG(remaining.Count)} remaining pixels");

                    if (nw > 0)
                    {
                        maxUsedLevel = level;
                    }
                } //for each level

                numFailed += remaining.Count;
                lock (failed)
                {
                    failed.AddRange(remaining);
                }

                InterlockedExtensions.Max(ref numFallbacks, maxUsedLevel);

                info($"finished batch {batch + 1}/{numBatches}: " +
                     $"backprojected {Fmt.KMG(numWinners)} pixels from preference 0 to " +
                     $"{maxUsedLevel} surface observations, {Fmt.KMG(numFailed)} failed ({pt.HMSR})");
            }); //for each batch

            stats.BackprojectedSurfacePixels = backprojectedSurfacePixels;
            stats.NumFallbacks = numFallbacks;

            int nf = failed != null ? failed.Count : samplePoints.Count;
            if (nf > 0)
            {
                info($"failed to backproject {Fmt.KMG(nf)} pixels to surface observations");
                if (orbitalImage != null)
                {
                    var indices = failed ?? Enumerable.Range(0, samplePoints.Count);
                    info($"attempting to backproject {Fmt.KMG(indices.Count())} pixels into orbital image");
                    var wl = BackprojectOrbitalObs(opts.occlusionScene, orbitalImage, opts.meshToOrbital,
                                                   opts.skyDirInMesh, samplePoints, indices, results, opts.verbose);
                    if (opts.verbose)
                    {
                        verbose($"backprojected {Fmt.KMG(wl.winners.Count)} pixels into orbital observation, " +
                                $"{Fmt.KMG(wl.losers.Count)} failed: " + wl.GetStats());
                    }
                    stats.BackprojectedOrbitalPixels = wl.winners.Count;
                    failed = wl.losers;
                }
                else
                {
                    info("no orbital image");
                    failed = failed ?? Enumerable.Range(0, samplePoints.Count).ToList();
                }
                foreach (var i in failed)
                {
                    results[SubpixelToPixel(samplePoints[i].Pixel)] = new ObsPixel();
                    stats.BackprojectMissingPixels++;
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
            var comparator = new RoverObservationComparator(mission);
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

        public static void DebugWriteCoverageImage(Options opts, SceneCaster debugTileOcclusion, Observation obs,
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

        public class WinnersAndLosers
        {
            public List<int> winners, losers;

            public int numOccluded;
            public int numMasked;
            public int numOutOfFrame;
            public int numCameraModelExceptions;
            public int numOutOfHull;
            public int numNonFinite;
            
            public WinnersAndLosers(int capacity)
            {
                winners = new List<int>(Math.Max(capacity, 1));
                losers = new List<int>(Math.Max(capacity, 1));
            }

            public WinnersAndLosers Add(int idx, bool winner)
            {
                if (winner)
                {
                    winners.Add(idx);
                }
                else
                {
                    losers.Add(idx);
                }
                return this;
            }

            public WinnersAndLosers AddLocked(WinnersAndLosers other)
            {
                lock (this)
                {
                    winners.AddRange(other.winners);
                    losers.AddRange(other.losers);
                    numOccluded += other.numOccluded;
                    numMasked += other.numMasked;
                    numOutOfFrame += other.numOutOfFrame;
                    numCameraModelExceptions += other.numCameraModelExceptions;
                    numOutOfHull += other.numOutOfHull;
                    numNonFinite += other.numNonFinite;
                }
                return this;
            }

            public string GetStats()
            {
                return $"{Fmt.KMG(numOccluded)} occluded, {Fmt.KMG(numMasked)} masked, " +
                    $"{Fmt.KMG(numOutOfFrame)} out of frame, " +
                    $"{Fmt.KMG(numCameraModelExceptions)} camera model exceptions, " +
                    $"{Fmt.KMG(numOutOfHull)} out of hull, {Fmt.KMG(numNonFinite)} non finite";
            }
        }

        //lowest level function that takes a set of PixelPoints to backproject
        //and attempts to backproject them into an observation image
        //if indices is null then backprojects all samplePoints
        //always adds winning pixels to results
        //returns partition of indices into winners losers, or null if indices is null
        public static WinnersAndLosers CoreBackproject(SceneCaster occlusionScene, Image mask, ConvexHull obsHullInMesh,
                                                       Matrix meshToObs, Matrix obsToMesh, CameraModel camera,
                                                       Observation obs, List<PixelPoint> samplePoints,
                                                       IEnumerable<int> indices, IDictionary<Pixel, ObsPixel> results,
                                                       double raycastTolerance = TexturingDefaults.RAYCAST_TOLERANCE,
                                                       double maxGlancingAngleCosine = 0, bool stats = false)
        {
            int ni = indices != null ? indices.Count() : 0;
            var winners = new WinnersAndLosers(ni);
            indices = indices ?? Enumerable.Range(0, samplePoints.Count);
#if NO_PARALLEL_RAYCASTS
            Serial.ForEach(indices, index =>
#else
            //an earlier implementation used ConcurrentBag instead of thread locals, but it had signficantly worse perf
            int nc = Math.Max(CoreLimitedParallel.GetMaxCores(), 1);
            CoreLimitedParallel.ForEach(indices, () => new WinnersAndLosers(ni / nc), (index, threadWinners) =>
#endif
            {
                var sample = samplePoints[index];
                bool winner = false;
                if (sample.Point.IsFinite()) //sampleTransform (used e.g. by BuildSkySphere) can kill points
                {
#if BACKPROJECT_CHECK_HULL
                    //testing the frustum hull can be a bit expensive
                    //camera.Project() gives a better answer with reasonable perf
                    //in the nonlin case the hull can be poorly fitting
                    if (obsHullInMesh.Contains(sample.Point))
                    {
#endif
                        try
                        {
                            Vector2 px = camera.Project(Vector3.Transform(sample.Point, meshToObs), out double range);
                            if (range > 0 && px.X >= 0 && px.X < obs.Width && px.Y >= 0 && px.Y < obs.Height)
                            {
                                //test if rover masked or missing data
                                //any neighbor pixels that are zero will cause the bilinear sample to be less than 1
                                //mask: 0 means bad, 1 means good (opposite of Image.Mask)
                                if (mask == null || mask.BilinearSample(0, (float)px.Y, (float)px.X) >= 1)
                                {
                                    //raycast the scene to test if the desired position is occluded by terrain
                                    if (!IsOccluded(camera, obsToMesh, px, occlusionScene, range,
                                                    raycastTolerance, maxGlancingAngleCosine))
                                    {
                                        results[SubpixelToPixel(sample.Pixel)] = new ObsPixel(obs, px);
                                        winner = true;
                                    }
                                    else if (stats)
                                    {
                                        Interlocked.Increment(ref winners.numOccluded);
                                    }
                                }
                                else if (stats)
                                {
                                    Interlocked.Increment(ref winners.numMasked);
                                }
                            }
                            else if (stats)
                            {
                                Interlocked.Increment(ref winners.numOutOfFrame);
                            }
                        }
                        catch (CameraModelException) //happens infrequently
                        {
                            if (stats) Interlocked.Increment(ref winners.numCameraModelExceptions);
                        }
#if BACKPROJECT_CHECK_HULL
                    }
                    else if (stats)
                    {
                        Interlocked.Increment(ref winners.numOutOfHull);
                    }
#endif
                }
                else if (stats)
                {
                    Interlocked.Increment(ref winners.numNonFinite);
                }
#if NO_PARALLEL_RAYCASTS
                winners.Add(index, winner);
            });
#else
                return threadWinners.Add(index, winner);
            },
            threadWinners => winners.AddLocked(threadWinners));
#endif
            return winners;               
        }

        //simpler wrapper on CoreBackproject() that just backprojects all samplePoints and returns results
        public static Dictionary<Pixel, ObsPixel>
            CoreBackproject(SceneCaster occlusionScene, Image mask, ConvexHull obsHullInMesh, Matrix obsToMesh,
                            CameraModel camera, Observation obs, List<PixelPoint> samplePoints)
        {
            var results = new Dictionary<Pixel, ObsPixel>();
            CoreBackproject(occlusionScene, mask, obsHullInMesh, Matrix.Invert(obsToMesh), obsToMesh, camera, obs,
                            samplePoints, null, results);
            return results;
        }

        public static WinnersAndLosers BackprojectSurfaceObs(PipelineCore pipeline, Project project,
                                                             SceneCaster occlusionScene, RoverMasker masker,
                                                             double raycastTolerance, double maxGlancingAngleCosine,
                                                             Context ctx, List<PixelPoint> samplePoints,
                                                             IEnumerable<int> indices,
                                                             IDictionary<Pixel, ObsPixel> results, bool stats)
        {
            Image mask = ImageMasker.GetOrCreateMask(pipeline, project, ctx.Obs, masker, ctx.MaskObs); //cached
            return CoreBackproject(occlusionScene, mask, ctx.FrustumHull, ctx.MeshToObs, ctx.ObsToMesh, ctx.CameraModel,
                                   ctx.Obs, samplePoints, indices, results, raycastTolerance, maxGlancingAngleCosine,
                                   stats);
        }

        public static WinnersAndLosers BackprojectOrbitalObs(SceneCaster occlusionScene, Observation orbitalObs,
                                                             Matrix meshToOrbital, Vector3 skyDirInMesh,
                                                             List<PixelPoint> samplePoints, IEnumerable<int> indices,
                                                             IDictionary<Pixel, ObsPixel> results, bool stats)
        {
            int ni = indices.Count();
            var winners = new WinnersAndLosers(ni);
#if NO_PARALLEL_RAYCASTS
            Serial.ForEach(indices, index =>
#else
            int nc = Math.Max(CoreLimitedParallel.GetMaxCores(), 1);
            CoreLimitedParallel.ForEach(indices, () => new WinnersAndLosers(ni / nc), (index, threadWinners) =>
#endif
            {
                var sample = samplePoints[index];
                bool winner = false;
                if (sample.Point.IsFinite()) //sampleTransform (used e.g. by BuildSkySphere) can kill points
                {
                    var rayToSky = new Ray(sample.Point, skyDirInMesh);
                    if (occlusionScene == null ||
                        occlusionScene.RaycastDistance(rayToSky, TexturingDefaults.RAYCAST_TOLERANCE) == null)
                    {
                        try
                        {
                            var px = orbitalObs.CameraModel.Project(Vector3.Transform(sample.Point, meshToOrbital));
                            if (px.X >= 0 && px.X < orbitalObs.Width && px.Y >= 0 && px.Y < orbitalObs.Height)
                            {
                                results[SubpixelToPixel(sample.Pixel)] = new ObsPixel(orbitalObs, px);
                                winner = true;
                            }
                            else if (stats)
                            {
                                Interlocked.Increment(ref winners.numOutOfFrame);
                            }
                        }
                        catch (CameraModelException)
                        {
                            winner = false; //happens infrequently, but not in frame
                            if (stats)
                            {
                                Interlocked.Increment(ref winners.numCameraModelExceptions);
                            }
                        }
                    }
                    else if (stats)
                    {
                        Interlocked.Increment(ref winners.numOccluded);
                    }
                }
                else if (stats)
                {
                    Interlocked.Increment(ref winners.numNonFinite);
                }
#if NO_PARALLEL_RAYCASTS
                winners.Add(index, winner);
            });
#else
                return threadWinners.Add(index, winner);
            },
            threadWinners => winners.AddLocked(threadWinners));
#endif
            return winners;               
        }

        public static bool InFrame(Vector3 meshPoint, Backproject.Context context, out Vector2 px)
        {
            try
            {
                px = context.CameraModel.Project(Vector3.Transform(meshPoint, context.MeshToObs), out double range);
                return range > 0 && px.X >= 0 && px.X < context.Obs.Width && px.Y >= 0 && px.Y < context.Obs.Height;
            }
            catch (CameraModelException)
            {
                px = new Vector2(double.NaN, double.NaN);
                return false; //happens infrequently, but not in frame
            }
        }
        
        public static bool InFrame(Vector3 meshPoint, Backproject.Context context)
        {
            return InFrame(meshPoint, context, out Vector2 px);
        }

        public static Ray? GetRayToMesh(CameraModel camera, Matrix obsToMesh, Vector2 pixel)
        {
            try
            {
                //get ray from camera through pixel associated with meshPos
                Ray rayCamToMeshInObsFrame = camera.Unproject(pixel);
                
                // convert from observation frame (typically rover_nav) to mesh (output frame, typically "root")
                Ray rayCamToMesh = new Ray(Vector3.Transform(rayCamToMeshInObsFrame.Position, obsToMesh),
                                           Vector3.TransformNormal(rayCamToMeshInObsFrame.Direction, obsToMesh));
                
                return rayCamToMesh;
            }
            catch (CameraModelException)
            {
                return null; //happens infrequently but failed to get ray for pixel
            }
        }

        public static bool IsObstructed(Vector3 meshPoint, SceneCaster occlusionScene, Backproject.Context context,
                                        double raycastTolerance = TexturingDefaults.RAYCAST_TOLERANCE)
        {
            try
            {
                var px = context.CameraModel.Project(Vector3.Transform(meshPoint, context.MeshToObs), out double range);
                bool ok = range > 0 && px.X >= 0 && px.X < context.Obs.Width && px.Y >= 0 && px.Y < context.Obs.Height;
                return ok && IsOccluded(context.CameraModel, context.ObsToMesh, px, occlusionScene, range, 
                                        raycastTolerance);
            }
            catch (CameraModelException)
            {
                return false; //happens infrequently, but not in frame
            }
        }

        public static bool IsOccluded(CameraModel camera, Matrix obsToMesh, Vector2 pixel,
                                      SceneCaster occlusionScene, double rangeMeshToImage,
                                      double raycastTolerance = TexturingDefaults.RAYCAST_TOLERANCE,
                                      double maxGlancingAngleCosine = 0)
        {
            if (occlusionScene == null)
            {
                return false;
            }

            Ray? rayCamToMesh = GetRayToMesh(camera, obsToMesh, pixel);
            if (!rayCamToMesh.HasValue)
            {
                return false;
            }
            
            var hit = occlusionScene.Raycast(rayCamToMesh.Value);

            if (hit == null)
            {
                return false; //ray did not hit scene
            }

            if (hit.Distance < (rangeMeshToImage - raycastTolerance))
            {
                return true; //if hit something else before camera, occluded
            }

            Vector3 n = hit.PointNormal.HasValue ? hit.PointNormal.Value : hit.FaceNormal;

            if (maxGlancingAngleCosine > 0 && Vector3.Dot(-rayCamToMesh.Value.Direction, n) < maxGlancingAngleCosine)
            {
                return true; //glancing angle too great
            }

            return false;
        }

        public static Vector3? RaycastMesh(CameraModel camera, Matrix obsToMesh, Vector2 pixel,
                                           SceneCaster occlusionScene, SceneCaster meshCaster, BoundingBox meshBounds,
                                           double raycastTolerance = TexturingDefaults.RAYCAST_TOLERANCE)
        {            
            Ray? rayCamToMesh = GetRayToMesh(camera, obsToMesh, pixel);
            if (!rayCamToMesh.HasValue)
            {
                return null;
            }

            //check if pixel ray hit the mesh
            Vector3? meshPos = meshCaster.RaycastPosition(rayCamToMesh.Value);
            if (!meshPos.HasValue)
            {
                return null;
            }

            //meshBounds may be a subset of the bounds of meshCaster
            //which is one way to ask for raycasts only on a subset of a big mesh
            //fuzzy is important for meshes with degnerate bounds, e.g. all vertices coplanar on an axis aligned plane
            if (!meshBounds.FuzzyContainsPoint(meshPos.Value, 1e-6))
            {
                return null; //ray was occluded by some other part of meshCaster than the part we're interested in
            }

            //another way to ask for raycasts on a subset of a big mesh
            //is to have meshCaster represent a subset of occlusionScene
            //this can also be used to just raycast against one mesh and occlude with a different one entirely
            //e.g. for sky sphere meshCaster is a tile on the skysphere and occlusionScene represents the terrain mesh
            if (occlusionScene != null && occlusionScene != meshCaster &&
                IsOccluded(camera, obsToMesh, pixel, occlusionScene,
                           Vector3.Distance(rayCamToMesh.Value.Position, meshPos.Value), raycastTolerance))
            {
                return null;
            }

            return meshPos;
        }

        public static Vector3? RaycastMesh(CameraModel camera, Matrix obsToMesh, Vector2 pixel, SceneCaster sc)
        {
            Ray? rayCamToMesh = GetRayToMesh(camera, obsToMesh, pixel);
            if (!rayCamToMesh.HasValue)
            {
                return null;
            }
            return sc.RaycastPosition(rayCamToMesh.Value);
        }
     
        public static IDictionary<string, ConvexHull> //indexed by observation name
            BuildFrustumHulls(PipelineCore pipeline, FrameCache frameCache, string outputFrame, bool usePriors,
                              bool onlyAligned, IEnumerable<RoverObservation> imgObservations, Project project,
                              bool rebuild = false, bool noSave = false, double farClip = 20)
        {
            int no = imgObservations.Count();

            pipeline.LogInfo("building{0} frustum hulls for {1} observations", rebuild ? "" : " or loading", no);

            var obsToHull = new ConcurrentDictionary<string, ConvexHull>();

            int np = 0, nc = 0, nl = 0, nf = 0;
            CoreLimitedParallel.ForEachNoPartition(imgObservations, obs =>
            {
                if (!rebuild && obs.HullGuid != Guid.Empty && obs.HullFarClip == farClip)
                {
                    var meshProd = pipeline.GetDataProduct<PlyGZDataProduct>(project, obs.HullGuid, noCache: true);
                    var loadedHull = ConvexHull.FromConvexMesh(meshProd.Mesh);
                    obsToHull.AddOrUpdate(obs.Name, _ => loadedHull, (_, __) => loadedHull);
                    Interlocked.Increment(ref nc);
                    Interlocked.Increment(ref nl);
                    return;
                }

                Interlocked.Increment(ref np);

                pipeline.LogDebug("computing frustum hull for observation {0}, processing {1} in parallel, " +
                                  "completed {2}/{3}", obs.Name, np, nc, no);

                var meshObs = new WedgeObservations() { Texture = obs };
                var opts = new WedgeObservations.MeshOptions()
                { Frame = outputFrame, UsePriors = usePriors, OnlyAligned = onlyAligned };
                var hull = meshObs.BuildFrustumHull(pipeline, frameCache, opts, uncertaintyInflated: false,
                                                    farClip: farClip);
                if (hull != null)
                {
                    obsToHull.AddOrUpdate(obs.Name, _ => hull, (_, __) => hull);
                    if (!noSave)
                    {
                        var hullProd = new PlyGZDataProduct(hull.Mesh);
                        pipeline.SaveDataProduct(project, hullProd, noCache: true);
                        obs.HullGuid = hullProd.Guid;
                        obs.HullFarClip = farClip;
                        obs.Save(pipeline);
                    }
                    Interlocked.Increment(ref nc);
                }
                else
                {
                    Interlocked.Increment(ref nf);
                    pipeline.LogWarn("failed to build convex hull for observation {0}", obs.Name);
                }

                Interlocked.Decrement(ref np);
            });

            pipeline.LogInfo("built convex hulls for {0} observations, {1} cached, {2} failed", nc - nl, nl, nf);

            return obsToHull;
        }

        //helper fucntion to convert from subpixel coordinates to integer pixel texture addresses
        public static Pixel SubpixelToPixel(Vector2 subPixel)
        {
            return new Pixel((int)subPixel.Y, (int)subPixel.X);
        }
    }
}
