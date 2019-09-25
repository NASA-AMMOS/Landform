using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using log4net;
using Emgu.CV.Structure;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Imaging.Emgu;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class DeltaRangeImage
    {
        // fills a texture with the difference in the per-pixel range of a src point cloud and dst point cloud 
        // designed to give an coarse visual estimate of how well cameras are aligned
        public static Image Create(PipelineCore pipeline, RoverMasker masker, MeshObservations srcObs,
                                   MeshObservations dstObs, FrameCache frameCache, bool usePriors, bool noPriors)
        {
            //load images
            var srcPointsRaw = pipeline.LoadImage(srcObs.Points.Url);
            var srcPoints = (new PDSImage(srcPointsRaw)).ConvertPoints();

            var dstPointsRaw = pipeline.LoadImage(dstObs.Points.Url);
            var dstPoints = (new PDSImage(dstPointsRaw)).ConvertPoints();

            if (srcPoints == null || dstPoints == null)
            {
                return null;
            }

            srcPoints.UnionMask(masker.LoadOrBuild(pipeline, srcObs.Mask != null ? srcObs.Mask.Url : null,
                                                   srcPointsRaw.Metadata as PDSMetadata),
                                new float[] { 0 });

            dstPoints.UnionMask(masker.LoadOrBuild(pipeline, dstObs.Mask != null ? dstObs.Mask.Url : null,
                                                   dstPointsRaw.Metadata as PDSMetadata),
                                new float[] { 0 });

            //get camera model
            Image dstImg = pipeline.LoadImage(dstObs.Texture.Url);
            PDSParser dstParser = new PDSParser((PDSMetadata)dstImg.Metadata);
            CameraModel dstCamera = dstParser.metadata.CameraModel;

            var srcToDst = frameCache.GetObservationTransform(srcObs.Points, dstObs.Points, usePriors, noPriors);
            if (srcToDst == null)
            {
                return null;
            }

            var meshOpts = new MeshObservations.MeshOptions() { Frame = "rover", UsePriors = usePriors };
            var dstHull = dstObs.BuildFrustumHull(pipeline, frameCache, meshOpts, uncertaintyInflated: false);

            //project points of src texture into dst
            Image deltaRangeImg = new Image(1, dstObs.Texture.Width, dstObs.Texture.Height);
            deltaRangeImg.CreateMask(true);

            bool anyValid = false;
            for (int idxSrcRow = 0; idxSrcRow < srcObs.Texture.Height; idxSrcRow++)
            {
                for (int idxSrcCol = 0; idxSrcCol < srcObs.Texture.Width; idxSrcCol++)
                {
                    if (!srcPoints.IsValid(idxSrcRow, idxSrcCol))
                    {
                        continue;
                    }

                    Vector3 srcRoverPt = new Vector3(srcPoints[0, idxSrcRow, idxSrcCol],
                                                     srcPoints[1, idxSrcRow, idxSrcCol],
                                                     srcPoints[2, idxSrcRow, idxSrcCol]);
                    Vector3 srcPtInDst = Vector3.Transform(srcRoverPt, srcToDst.Mean);

                    //coarse test to ensure no errors at the far distant edges of camera models, or points behind the
                    //camera projecting to valid screen positions.
                    //NOTE: enforces the hull distance limit which may be too conservative
                    //also accuracy is poor for nonlinear camera models
                    if (!dstHull.Contains(srcPtInDst))
                    {
                        continue;
                    }

                    Vector2 dstPixel = dstCamera.Project(srcPtInDst, out double range);

                    int dstPixelX = (int)Math.Round(dstPixel.X);
                    int dstPixelY = (int)Math.Round(dstPixel.Y);

                    if (dstPixelX < 0 || dstPixelX >= dstObs.Texture.Width ||
                        dstPixelY < 0 || dstPixelY >= dstObs.Texture.Height)
                    {
                        continue;
                    }

                    //Issue #476: properly handle spreading data across fractional pixels (subpixel projection results) 
                    //and properly handle blending with existing data (coverage channel)

                    Vector3 dstRoverPt = new Vector3(dstPoints[0, dstPixelY, dstPixelX],
                                                     dstPoints[1, dstPixelY, dstPixelX],
                                                     dstPoints[2, dstPixelY, dstPixelX]);

                    Vector2 refDstPixel = dstCamera.Project(dstRoverPt, out double refRange);
                    int refDstPixelX = (int)Math.Round(refDstPixel.X);
                    int refDstPixelY = (int)Math.Round(refDstPixel.Y);

                    if (refDstPixelX < 0 || refDstPixelX >= dstObs.Texture.Width ||
                        refDstPixelY < 0 || refDstPixelY >= dstObs.Texture.Height)
                    {
                        continue;
                    }

                    if (!dstPoints.IsValid((int)refDstPixelY, (int)refDstPixelX))
                    {
                        continue;
                    }

                    if ((int)refDstPixelX != (int)dstPixelX || (int)refDstPixelY != (int)dstPixelY)
                    {
                        throw new Exception("range product points should map back to the same pixel it was pulled from");
                    }

                    deltaRangeImg[0, (int)dstPixel.Y, (int)dstPixel.X] = (float)Vector3.Distance(dstRoverPt, srcPtInDst);
                    deltaRangeImg.SetMaskValue((int)dstPixel.Y, (int)dstPixel.X, false);
                    anyValid = true;
                }
            }

            return anyValid ? deltaRangeImg : null;
        }

        public static Image CreatePreview(Image deltaRangeImage, int decimationBlocksize = 4,
                                          float[] previewDistanceBuckets = null, string colorScheme = "Blues",
                                          Vector3? backgroundColor = null)
        {
            if (previewDistanceBuckets == null)
            {
                previewDistanceBuckets = new float[] { 0.1f, 0.25f, 0.5f, 0.75f, 1.0f, 1.25f, 1.5f };
            }

            Vector3[] colors = BrewerColors.GetColors(colorScheme, previewDistanceBuckets.Length + 1);
                    
            if (!backgroundColor.HasValue)
            {
                backgroundColor = new Vector3(0.9, 0.9, 0.9);
            }

            Image preview = deltaRangeImage
                .Decimated(decimationBlocksize)
                .ColorizeScalarImage(previewDistanceBuckets,
                                     colors.Select(c => c.ToFloatArray()).ToArray(),
                                     backgroundColor.Value.ToFloatArray());

            preview = StampLegend(preview, previewDistanceBuckets, colors, backgroundColor.Value);

            preview.DeleteMask();

            return preview;
        }

        private static Image StampLegend(Image img, float[] previewDistanceBuckets, Vector3[] colorsLowToHigh,
                                         Vector3 backgroundColor)
        {
            //formatting parameters
            // if we need a more general layout api these can be exposed
            int largeSpacing = 16;
            int smallSpacing = 7;
            int colorChipWidth = 10;
            int frameWidth = 70;

            int legendDimColor = 3;
            Rgb textColor = new Rgb(40, 40, 40);
            Rgb bgColor = OPS.Imaging.Emgu.Extensions.ToEmguColor(backgroundColor.ToFloatArray());
            Rgb legendColor = new Rgb(Math.Max(0,bgColor.Red - legendDimColor),
                                      Math.Max(0, bgColor.Green - legendDimColor),
                                      Math.Max(0, bgColor.Blue - legendDimColor));

            //allocate expanded image and clear to background color
            System.Drawing.Size expandedImageSize = new System.Drawing.Size(frameWidth + img.Width, img.Height);
            Emgu.CV.Image<Rgb, byte> emguImg = new Emgu.CV.Image<Rgb, byte>(expandedImageSize);
            emguImg.Draw(new System.Drawing.Rectangle(new System.Drawing.Point(0, 0),
                                                      new System.Drawing.Size(frameWidth, img.Height)),
                         legendColor, -1);

            //draw legend

            System.Drawing.Point pt = new System.Drawing.Point(largeSpacing, largeSpacing);
            
            //catchall
            emguImg.Draw(new System.Drawing.Rectangle(new System.Drawing.Point(pt.X, pt.Y - (int)colorChipWidth / 2),
                                                      new System.Drawing.Size(colorChipWidth, colorChipWidth)),
                         OPS.Imaging.Emgu.Extensions.ToEmguColor(colorsLowToHigh.Last().ToFloatArray()), -1);
            emguImg.Draw("> " + previewDistanceBuckets[previewDistanceBuckets.Length - 1].ToString("F2") + "m",
                         new System.Drawing.Point(pt.X + colorChipWidth + smallSpacing, pt.Y),
                         Emgu.CV.CvEnum.FontFace.HersheySimplex, 0.2, textColor, 1);
            pt.Y += largeSpacing;

            for (int idx = previewDistanceBuckets.Length-1; idx >= 0; idx--)
            {
                Rgb color = OPS.Imaging.Emgu.Extensions.ToEmguColor(colorsLowToHigh[idx].ToFloatArray());
                emguImg.Draw(new System.Drawing.Rectangle(new System.Drawing.Point(pt.X,pt.Y - (int)colorChipWidth / 2),
                                                          new System.Drawing.Size(colorChipWidth, colorChipWidth)),
                             color, -1);
                emguImg.Draw("< " + previewDistanceBuckets[idx].ToString("F2") + "m",
                             new System.Drawing.Point(pt.X + colorChipWidth + smallSpacing, pt.Y),
                             Emgu.CV.CvEnum.FontFace.HersheySimplex, 0.2, textColor, 1);
                pt.Y += largeSpacing;
            }
            
            Image result = emguImg.ToOPSImage();
            emguImg.Dispose();

            result.Blit(img, frameWidth, 0);

            return result;
        }
    }
}
