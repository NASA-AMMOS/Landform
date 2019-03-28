using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;
using Microsoft.Xna.Framework;
using OPS.Imaging.Emgu;
using Emgu.CV.Structure;

namespace OPS.Pipeline
{
    [Verb("local-observation-products", HelpText = "create observation mesh and image products locally")]
    public class LocalObservationProductsOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "Project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "Only generate meshes for specific site drives, comma separated", Default = null)]
        public string OnlyForSiteDrives { get; set; }

        [Option(HelpText = "Output directory, or omit to save to project storage", Default = null)]
        public string OutputFolder { get; set; }

        [Option(HelpText = "Output coordinate frame: rover, sitedrive, or root", Default = "rover")]
        public string OutputFrame { get; set; }

        [Option(HelpText = "Create meshes for mastcam observations", Default = false)]
        public bool AllowMastcam { get; set; }

        [Option(HelpText = "Only create meshes for observations with normals", Default = false)]
        public bool RequireNormals { get; set; }

        [Option(HelpText = "Only create meshes for observations with textures", Default = false)]
        public bool RequireTextures { get; set; }

        [Option(HelpText = "Don't write wedge meshes", Default = false)]
        public bool NoWedgeMeshes { get; set; }

        [Option(HelpText = "Don't write observation images (and don't texture wedge meshes)", Default = false)]
        public bool NoImages { get; set; }

        [Option(HelpText = "Mesh format, e.g. ply, obj, help for list", Default = "ply")]
        public string MeshFormat { get; set; }

        [Option(HelpText = "Image format, e.g. png, jpg, help for list", Default = "jpg")]
        public string ImageFormat { get; set; }

        [Option(HelpText = "Create point clouds instead of triangle meshes", Default = false)]
        public bool PointCloud { get; set; }

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,Agisoft)", Default = null)]
        public string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)", Default = null)]
        public string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public bool UsePriors { get; set; }

        [Option(HelpText = "Mesh decimation blocksize", Default = 4)]
        public int DecimateMeshes { get; set; }

        [Option(HelpText = "Image decimation blocksize", Default = 2)]
        public int DecimateImages { get; set; }

        [Option(HelpText = "Max triangle aspect ratio", Default = 10)]
        public double MaxTriangleAspect { get; set; }

        [Option(HelpText = "Scale normals by confidence", Default = false)]
        public bool ScaleNormalsByConfidence { get; set; }

        [Option(HelpText = "Don't split output by site drive", Default = false)]
        public bool SuppressSiteDriveDirectories { get; set; }

        [Option(HelpText = "Mask image format, e.g. png, jpg, help for list", Default = "png")]
        public string MaskFormat { get; set; }

        [Option(HelpText = "Write camera frustum hull meshes", Default = false)]
        public bool WriteFrustumHullMeshes { get; set; }

        [Option(HelpText = "Write uncertainty inflated camera frustum hull meshes", Default = false)]
        public bool WriteUncertaintyInflatedFrustumHullMeshes { get; set; }

        [Option(HelpText = "Write all the things", Default = false)]
        public bool WriteAllTheThings { get; set; }

        [Option(HelpText = "Hide progress", Default = false)]
        public bool NoProgress { get; set; }

        [Option(HelpText = "Operate on cloud data", Default = false)]
        public bool Cloud { get; set; }

        [Option(HelpText = "Write delta range images", Default = false)]
        public bool WriteDeltaRangeImages { get; set;}
    } 

    public class LocalObservationProducts
    {
        private LocalObservationProductsOptions options;
        private PipelineCore pipeline;

        public LocalObservationProducts(LocalObservationProductsOptions options)
        {
            this.options = options;
            if (options.Cloud)
            {
                this.pipeline = new CloudPipeline(options, initQueues: false);
            }
            else
            {
                this.pipeline = new LocalPipeline(options);
            }
        }

        public int Run()
        {
            options.WriteFrustumHullMeshes |= options.WriteAllTheThings;
            options.WriteUncertaintyInflatedFrustumHullMeshes |= options.WriteAllTheThings;

            var project = Project.Find(pipeline, options.ProjectName);

            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }

            var outputFrame = options.OutputFrame.ToLower().Trim();
            if (!(new [] {"rover", "sitedrive", "root"}).Any(f => outputFrame == f))
            {
                pipeline.LogError("unknown output frame: " + outputFrame);
                return 1;
            }

            TransformSource[] parseSources(string sources)
            {
                return (sources ?? "")
                    .Split(',')
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => Enum.Parse(typeof(TransformSource), s.Trim(), ignoreCase: true))
                    .Cast<TransformSource>()
                    .ToArray();
            }
            var adjustedSources = parseSources(options.AdjustedTransformSources);
            var priorSources = parseSources(options.PriorTransformSources);

            string dir = outputFrame + "Frame";
            if (options.UsePriors)
            {
                dir += "/prior";
                if (priorSources.Length > 0)
                {
                    dir += "_" + String.Join("_", priorSources);
                }
            }
            else
            {
                dir += "/best";
                if (priorSources.Length > 0)
                {
                    dir += "_" + String.Join("_", priorSources);
                }
                if (adjustedSources.Length > 0)
                {
                    dir += "_" + String.Join("_", adjustedSources);
                }
            }
            string outputPath = pipeline.GetLocalDebugFolder(options.OutputFolder,
                                                             "alignment/ObservationProducts/" + dir,
                                                             project.Name);

            string meshExt = null;
            if (!options.NoWedgeMeshes || options.WriteFrustumHullMeshes ||
                options.WriteUncertaintyInflatedFrustumHullMeshes)
            {
                meshExt = MeshSerializers.Instance.CheckFormat(options.MeshFormat, pipeline);
                if (meshExt == null)
                {
                    return 0;
                }
                if  (!options.NoWedgeMeshes)
                {
                    pipeline.LogInfo("writing {0} wedge meshes to {1}", meshExt, outputPath);
                }
                if (options.WriteFrustumHullMeshes)
                {
                    pipeline.LogInfo("writing {0} hull meshes to {1}", meshExt, outputPath);
                } 
                if (options.WriteUncertaintyInflatedFrustumHullMeshes)
                {
                    pipeline.LogInfo("writing {0} uncertainty inflated hull meshes to {1}", meshExt, outputPath);
                } 
            }

            string imageExt = null;
            if (!options.NoImages)
            {
                imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, pipeline);
                if (imageExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} images to {1}", imageExt, outputPath);
            }

            var frameCache = new FrameCache(pipeline, options.ProjectName);
            Func<FrameTransform, bool> filterPrior =
                transform => priorSources.Length == 0 || priorSources.Any(s => s == transform.Source);
            Func<FrameTransform, bool> filterAdjusted =
                transform => adjustedSources.Length == 0 || adjustedSources.Any(s => s == transform.Source);
            frameCache.Preload(loadTransforms: true, transformFilter: ft => 
                               (!options.UsePriors || ft.IsPrior()) && //iff --usepriors only allow priors
                               ((ft.IsPrior() && filterPrior(ft)) || //iff --priorsources only allow specific priors
                                (!ft.IsPrior() && filterAdjusted(ft)))); //iff --adjustedsources only allow specific adj

            var observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache.Preload();

            var observations = Meshing.CollectMeshObservations(frameCache, observationCache, options.AllowMastcam,
                                                               options.RequireNormals, options.RequireTextures);

            SiteDrive getSiteDrive(MeshObservations obs)
            {
                var ro = obs.Points as RoverObservation;
                return new SiteDrive(ro.Site, ro.Drive);
            }

            SiteDrive[] siteDrives = (options.OnlyForSiteDrives ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => new SiteDrive(s.Trim()))
                .Cast<SiteDrive>()
                .ToArray();

            if (siteDrives.Length > 0)
            {
                observations = observations.Where(obs => siteDrives.Any(sd => sd == getSiteDrive(obs))).ToList();
            }
                                                  
            int no = observations.Count();
            string what = options.PointCloud ? "point clouds" : "triangle meshes";
            pipeline.LogInfo("computing {0} for {1} observations{2} under {3}", what, no,
                             siteDrives.Length > 0 ?
                             (" for site drive(s) " +
                              String.Join(",", siteDrives.Select(sd => sd.ToString()).Cast<string>().ToArray())) : "",
                             outputPath);

            double startSec = UTCTime.Now();
            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(observations, obs => { 

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("computing {0} for {1} observations in parallel, completed {2}/{3}",
                                         what, np, nc, no);
                    }

                    var mesh = options.PointCloud ?
                    Meshing.BuildPointCloud(pipeline, obs, frameCache, outputFrame, options.UsePriors,
                                            options.DecimateMeshes, options.ScaleNormalsByConfidence) :
                    Meshing.BuildOrganizedMesh(pipeline, obs, frameCache, outputFrame, options.UsePriors,
                                               options.DecimateMeshes, options.ScaleNormalsByConfidence,
                                               options.MaxTriangleAspect, !options.NoImages);

                    //we're running for multiple site drives in parallel so don't mutate outputPath
                    string tmpPath = outputPath;
                    if (!options.SuppressSiteDriveDirectories)
                    {
                        tmpPath += getSiteDrive(obs).ToString() + "/";
                    }

                    string obsName = obs.Points.Name;
                    string imageFilename = null;
                    if (!options.NoImages && obs.Texture != null && mesh.HasUVs)
                    {
                        imageFilename = obsName + imageExt;
                        var img = pipeline.LoadImage(obs.Texture.Url);
                        if (options.DecimateImages > 1)
                        {
                            img = img.Decimated(options.DecimateImages);
                        }
                        PathHelper.EnsureExists(tmpPath);
                        img.Save<byte>(tmpPath + imageFilename);
                    }

                    if (!options.NoWedgeMeshes)
                    {
                        PathHelper.EnsureExists(tmpPath);
                        mesh.Save(tmpPath + obsName + meshExt, imageFilename);
                    }
                      
                    if (options.WriteFrustumHullMeshes)
                    {
                        var hull = Meshing.BuildFrustumHull(pipeline, obs, frameCache, outputFrame, options.UsePriors,
                                                            uncertaintyInflated: false);
                        var path = tmpPath + "Frusta/";
                        PathHelper.EnsureExists(path);
                        hull.Mesh.Save(path + obsName + meshExt);
                    }

                    if (options.WriteUncertaintyInflatedFrustumHullMeshes)
                    {
                        var hull = Meshing.BuildFrustumHull(pipeline, obs, frameCache, outputFrame, options.UsePriors,
                                                            uncertaintyInflated: true);
                        var path = tmpPath + "InflatedFrusta/";
                        PathHelper.EnsureExists(path);
                        hull.Mesh.Save(path + obsName + meshExt);
                    }

                    if (options.WriteDeltaRangeImages)
                    {
                        string path = tmpPath + "DeltaRange/";
                        PathHelper.EnsureExists(path);

                        string pathPreview = tmpPath + "DeltaRangePreview/";
                        PathHelper.EnsureExists(pathPreview);

                        float[] previewDistanceBuckets = new float[] { 0.1f, 0.25f, 0.5f, 0.75f, 1.0f, 1.25f, 1.5f };
                        Vector3 [] colors = BrewerColors.GetColors("Blues", previewDistanceBuckets.Length + 1);
                       
                        foreach (var otherObs in observations)
                        {
                            if (obs == otherObs)
                                continue;

                            var overlap = Overlap.Find(pipeline, project.Name, otherObs.Texture.Name, obs.Texture.Name);
                            if (overlap == null)
                                continue;

                            Image deltaRangeImage = CreateDeltaRangeImage(otherObs, obs, frameCache, options.UsePriors);
                            if (deltaRangeImage != null)
                            {
                                string imageName = otherObs.Points.Name + "_in_" + obs.Points.Name;
                                deltaRangeImage.Save<float>(path + imageName + ".tif");

                                Vector3 backgroundColor = new Vector3(0.9, 0.9, 0.9);
                                Image deltaRangePreview = Image.ColorizeScalarImage(deltaRangeImage.Decimated(4), previewDistanceBuckets, colors.Select(c => c.ToFloatArray()).ToArray(), backgroundColor.ToFloatArray());
                                deltaRangePreview = StampLegend(deltaRangePreview, previewDistanceBuckets, colors, backgroundColor);
                                deltaRangePreview.DeleteMask();
                                deltaRangePreview.Save<byte>(pathPreview + imageName + ".png");
                            }
                        }
                    }

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });
            double totalSec = UTCTime.Now() - startSec;
            
            pipeline.LogInfo("generated meshes for {0} observations ({1:F3}s)", no, totalSec);

            return 0;
        }
       
        private Image StampLegend(Image img, float[] previewDistanceBuckets, Vector3[] colorsLowToHigh, Vector3 backgroundColor)
        {
            //formatting parameters
            // if we need a more general layout api these can be exposed
            int largeSpacingPixels = 16;
            int smallSpacingPixels = 7;
            int colorChipWidthPixels = 10;
            int frameWidthPixels = 70;

            int legendDimColor = 3;
            Rgb textColor = new Rgb(40, 40, 40);
            Rgb bgColor = OPS.Imaging.Emgu.Extensions.ToEmguColor(backgroundColor.ToFloatArray());
            Rgb legendColor = new Rgb(Math.Max(0,bgColor.Red - legendDimColor), Math.Max(0, bgColor.Green - legendDimColor), Math.Max(0, bgColor.Blue - legendDimColor));

            //allocate expanded image and clear to background color
            System.Drawing.Size expandedImageSize = new System.Drawing.Size(frameWidthPixels + img.Width, img.Height);
            Emgu.CV.Image<Rgb, byte> emguImg = new Emgu.CV.Image<Rgb, byte>(expandedImageSize);
            emguImg.Draw(new System.Drawing.Rectangle(new System.Drawing.Point(0, 0), new System.Drawing.Size(frameWidthPixels, img.Height)), legendColor, -1);

            //draw legend

            System.Drawing.Point pt = new System.Drawing.Point(largeSpacingPixels, largeSpacingPixels);
            
            //catchall
            emguImg.Draw(new System.Drawing.Rectangle(new System.Drawing.Point(pt.X, pt.Y - (int)colorChipWidthPixels / 2), new System.Drawing.Size(colorChipWidthPixels, colorChipWidthPixels)), OPS.Imaging.Emgu.Extensions.ToEmguColor(colorsLowToHigh.Last().ToFloatArray()), -1);
            emguImg.Draw("> " + previewDistanceBuckets[previewDistanceBuckets.Length - 1].ToString("F2") + "m", new System.Drawing.Point(pt.X + colorChipWidthPixels + smallSpacingPixels, pt.Y), Emgu.CV.CvEnum.FontFace.HersheySimplex, 0.2, textColor, 1);
            pt.Y += largeSpacingPixels;

            for (int idx = previewDistanceBuckets.Length-1; idx >= 0; idx--)
            {
                Rgb color = OPS.Imaging.Emgu.Extensions.ToEmguColor(colorsLowToHigh[idx].ToFloatArray());
                emguImg.Draw(new System.Drawing.Rectangle(new System.Drawing.Point(pt.X,pt.Y - (int)colorChipWidthPixels/2), new System.Drawing.Size(colorChipWidthPixels, colorChipWidthPixels)), color, -1);
                emguImg.Draw("< " + previewDistanceBuckets[idx].ToString("F2") + "m", new System.Drawing.Point(pt.X + colorChipWidthPixels + smallSpacingPixels, pt.Y), Emgu.CV.CvEnum.FontFace.HersheySimplex, 0.2, textColor, 1);
                pt.Y += largeSpacingPixels;
            }
            
            Image result = emguImg.ToOPSImage();
            emguImg.Dispose();

            result.Composite(img,0,frameWidthPixels);

            return result;
        }

        // fills a texture with the difference in the per-pixel range of a src point cloud and dst point cloud 
        // designed to give an coarse visual estimate of how well cameras are aligned
        private Image CreateDeltaRangeImage(MeshObservations srcObs, MeshObservations dstObs, FrameCache frameCache, bool usePriors)
        {
            //load images
            Meshing.LoadOrGenerateMeshImages(this.pipeline, srcObs, 1, false, out Image srcPoints, out Image srcNormals, out Image srcMask);
            srcPoints.UnionMask(srcMask, new float[] { 0 });

            Meshing.LoadOrGenerateMeshImages(this.pipeline, dstObs, 1, false, out Image dstPoints, out Image dstNormals, out Image dstMask);
            dstPoints.UnionMask(dstMask, new float[] { 0 });

            //get camera model
            Image dstImg = pipeline.LoadImage(dstObs.Texture.Url);
            PDSParser dstParser = new PDSParser((PDSMetadata)dstImg.Metadata);
            CameraModel dstCamera = dstParser.metadata.CameraModel;
 
            var srcObsToDstObs = Meshing.GetTransform(srcObs.Points.FrameName, dstObs.Points.FrameName, frameCache, usePriors).Mean;
            var dstHull = Meshing.BuildFrustumHull(pipeline, dstObs, frameCache, dstObs.Points.FrameName, usePriors, uncertaintyInflated: false);

            //project points of src texture into dst
            Image deltaRangeImg = new Image(1, dstObs.Texture.Width, dstObs.Texture.Height);
            deltaRangeImg.CreateMask(true);

            bool anyValid = false;
            for (int idxSrcRow = 0; idxSrcRow < srcObs.Texture.Height; idxSrcRow++)
            {
                for (int idxSrcCol = 0; idxSrcCol < srcObs.Texture.Width; idxSrcCol++)
                {
                    if (srcPoints.IsInvalid(idxSrcRow,idxSrcCol))
                        continue;

                    Vector3 srcRoverPt = new Vector3(srcPoints[0, idxSrcRow, idxSrcCol], srcPoints[1, idxSrcRow, idxSrcCol], srcPoints[2, idxSrcRow, idxSrcCol]);
                    Vector3 srcPtInDst = Vector3.Transform(srcRoverPt, srcObsToDstObs);

                    //coarse test to ensure no errors at the far distant edges of camera models, or points behind the camera 
                    // projecting to valid screen positions. NOTE: enforces the hull distance limit which may be too conservative
                    // also accuracy is poor for nonlinear camera models
                    if (!dstHull.Contains(srcPtInDst))
                        continue;

                    Vector2 dstPixel = dstCamera.Project(srcPtInDst, out double range);

                    int dstPixelX = (int)Math.Round(dstPixel.X);
                    int dstPixelY = (int)Math.Round(dstPixel.Y);

                    if (dstPixelX < 0 || dstPixelX >= dstObs.Texture.Width ||
                        dstPixelY < 0 || dstPixelY >= dstObs.Texture.Height)
                        continue;

                    //TODO: properly handle spreading data across fractional pixels (subpixel projection results)
                    //TODO: properly handle blending with existing data (coverage channel)
               
                    Vector3 dstRoverPt = new Vector3(dstPoints[0, dstPixelY, dstPixelX], dstPoints[1, dstPixelY, dstPixelX], dstPoints[2, dstPixelY, dstPixelX]);

                    Vector2 refDstPixel = dstCamera.Project(dstRoverPt, out double refRange);
                    int refDstPixelX = (int)Math.Round(refDstPixel.X);
                    int refDstPixelY = (int)Math.Round(refDstPixel.Y);

                    if (refDstPixelX < 0 || refDstPixelX >= dstObs.Texture.Width ||
                        refDstPixelY < 0 || refDstPixelY >= dstObs.Texture.Height)
                        continue;

                    if (dstMask.IsInvalid((int)refDstPixelY, (int)refDstPixelX))
                        continue;

                    if ((int)refDstPixelX != (int)dstPixelX || (int)refDstPixelY != (int)dstPixelY)
                        throw new Exception("range product points should map back to the same pixel it was pulled from");

                    deltaRangeImg[0, (int)dstPixel.Y, (int)dstPixel.X] = (float)Math.Abs(range - refRange);
                    deltaRangeImg.SetMaskValue((int)dstPixel.Y, (int)dstPixel.X, false);
                    anyValid = true;
                }
            }

            return anyValid ? deltaRangeImg : null;
        }
    }
}
