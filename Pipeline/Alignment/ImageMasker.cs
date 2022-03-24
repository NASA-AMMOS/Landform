using System;
using System.Linq;
using JPLOPS.Imaging;
using JPLOPS.Pipeline.AlignmentServer;

namespace JPLOPS.Pipeline
{
    public static class ImageMasker
    {
        public const int DEF_MASK_BORDER = 10;
        public const int DEF_CACHE_SIZE = 100;

        /// <summary>
        /// this is a little confusing because Landform Images can have boolean mask arrays
        /// but the feature detection APIs don't respect those
        /// partly because some of those APIs send images to OpenCV
        /// instead, we need a separate mask binary image which is 0 for masked pixels
        ///
        /// the image mask we use for feature detection purposes combines a number of things
        /// 1) rover mask
        /// 2) invalid pixels in the original image (e.g. pixels equal to the PDS header MissingConstant)
        /// 3) masked pixels the original image (e.g. due to a user mask override image)
        /// 4) inset borders of the original image (image borders sometimes have solid bars)
        /// </summary>
        public static Image MakeMask(PipelineCore pipeline, RoverMasker masker, string roverMaskUrl, Image img,
                                     Image dbgImg = null)
        {
            bool drawDebug = dbgImg != null;
            if (drawDebug && dbgImg.Bands < 3)
            {
                float[] band0 = dbgImg.GetBandData(0);
                while (dbgImg.Bands < 3)
                {
                    Array.Copy(band0, dbgImg.GetBandData(dbgImg.AddBand()), band0.Length);
                }
            }

            Image mask = masker.LoadOrBuild(pipeline, roverMaskUrl, img.Metadata as PDSMetadata);

            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (drawDebug && mask[0, row, col] == 0)
                    {
                        dbgImg[0, row, col] = 1; //rover mask = red tint
                    }

                    //propagate masked pixels from original image to mask
                    if (!img.IsValid(row, col))
                    {
                        mask[0, row, col] = 0;
                        if (drawDebug)
                        {
                            dbgImg[1, row, col] = 1; //masked/invalid/border = green tint
                        }
                    }
                }
            }

            //propagate invalid pixels from original image to mask
            int borderPixels = DEF_MASK_BORDER;
            if (img.Metadata is PDSMetadata)
            {
                var parser = new PDSParser((PDSMetadata)img.Metadata);

                //nominal missing constant value is 0, MSL SIS
                float[] missing = new float[] { 0.0f};

                if (parser.HasMissingConstant)
                {
                    missing = parser.MissingConstant;
                }
                //ROASTT18 wart: single float missing constant for 3 channel navcam
                if (missing.Count() == 1 && img.Bands > 1)
                {
                    missing = Enumerable.Repeat<float>(missing.First(), img.Bands).ToArray();
                }

                //nominal invalid constant value is 0, MSL SIS
                float[] invalid = new float[] { 0.0f };

                if (parser.HasInvalidConstant)
                {
                    invalid = parser.InvalidConstant;
                }

                //ROASTT19 wart: single float missing constant for 3 channel navcam
                if (invalid.Count() == 1 && img.Bands > 1)
                {
                    invalid = Enumerable.Repeat<float>(invalid.First(), img.Bands).ToArray();
                }

                //we could do it this way, but it's just a few more lines to avoid allocating the mask array
                //mask.UnionMask(img, missing);
                //mask.SetValuesForMaskedData(new float[] { 0 });
                for (int row = 0; row < img.Height; row++)
                {
                    for (int col = 0; col < img.Width; col++)
                    {
                        if (img.BandValuesEqual(row, col, missing) || img.BandValuesEqual(row, col, invalid))
                        {
                            mask[0, row, col] = 0;
                            if (drawDebug)
                            {
                                dbgImg[1, row, col] = 1; //masked/invalid/border = green tint
                            }
                        }
                    }
                }

                borderPixels = masker.GetBorderPixels(parser);
            }

            //add borders to mask
            int border = Math.Min(mask.Height / 2, Math.Min(mask.Width / 2, borderPixels));
            for (int b = 0; b < border; b++)
            {
                //whole row
                for(int col = 0; col < mask.Width; col++)
                {
                    mask[0, b, col] = 0;
                    mask[0, mask.Height - 1 - b, col] = 0;
                    if (drawDebug)
                    {
                        dbgImg[1, b, col] = 1; //masked/invalid/border = green tint
                        dbgImg[1, mask.Height - 1 - b, col] = 1;
                    }
                }

                //whole column
                for (int row = 0; row < mask.Height; row++)
                {
                    mask[0, row, b] = 0;
                    mask[0, row, mask.Width - 1 - b] = 0;
                    if (drawDebug)
                    {
                        dbgImg[1, row, b] = 1; //masked/invalid/border = green tint
                        dbgImg[1, row, mask.Width - 1 - b] = 1;
                    }
                }
            }

            return mask;
        }

        public static Image GetOrCreateMask(PipelineCore pipeline, Project project, Observation imageObs,
                                            RoverMasker masker, Observation maskObs = null, Image img = null)
        {
            return GetOrCreateMask(pipeline, project, imageObs, masker, maskObs != null ? maskObs.Url : null, img);
        }

        public static Image GetOrCreateMask(PipelineCore pipeline, Project project, Observation imageObs,
                                            RoverMasker masker, string roverMaskUrl = null, Image img = null)
        {
            if (imageObs.MaskGuid != Guid.Empty)
            {
                //let the masks stay in the LRU cache here
                //they might be used in subsequent stages
                //and most commands clear the image cache entirely at some point
                return pipeline.GetDataProduct<PngDataProduct>(project, imageObs.MaskGuid).Image;
            }
            else
            {
                if (img == null)
                {
                    //disable LRU image cache for mask images
                    //if GetOrCreateMask() gets called again it should return the cached mask product
                    //other codepaths may call RoverMasker.LoadOrBuild() directly
                    //but that would typically be to make a mask for a geometry image (XYZ, RNG, UVW)
                    //which is somewhat different (e.g. does not include the mask borders)
                    //than the we create and cache here, which is typically used for feature detection and texturing
                    //most executions only use one or the other kind of masks
                    //so the possible speed improvement of caching them both here and in RoverMasker
                    //is probably not worth the memory consumption of caching the masks
                    //(also best cache coherence would require reversing order of second access)
                    img = pipeline.LoadImage(imageObs.Url, noCache: true);
                }
                Image mask = MakeMask(pipeline, masker, roverMaskUrl, img);
                var maskProd = new PngDataProduct(mask);
                pipeline.SaveDataProduct(project, maskProd); //leave cache enabled
                imageObs.MaskGuid = maskProd.Guid;
                imageObs.Save(pipeline);
                return mask;
            }
        }
    }
}
