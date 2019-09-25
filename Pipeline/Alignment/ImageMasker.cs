using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
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
        public static Image MakeMask(PipelineCore pipeline, RoverMasker masker, string roverMaskUrl, Image img)
        {
            //do not mutate rover mask if it's loaded from mission product (clone: true)
            Image mask = masker.LoadOrBuild(pipeline, roverMaskUrl, img.Metadata as PDSMetadata, clone: true);

            //propagate masked pixels from original image to mask
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (!img.IsValid(row, col))
                    {
                        mask[0, row, col] = 0;
                    }
                }
            } 

            //propagate invalid pixels from original image to mask
            if (img.Metadata is PDSMetadata)
            {
                var parser = new PDSParser((PDSMetadata)img.Metadata);
                if (parser.HasMissingConstant)
                {
                    float[] missing = parser.MissingConstant.Select(x => (float)x).ToArray();

                    //ROASTT: single float missing constant for 3 channel navcam
                    if(missing.Count() == 1 && img.Bands > 1)
                    {
                        missing = Enumerable.Repeat<float>(missing.First(), img.Bands).ToArray();
                    }

                    //we could do it this way, but it's just a few more lines to avoid allocating the mask array
                    //mask.UnionMask(img, missing);
                    //mask.SetValuesForMaskedData(new float[] { 0 });
                    for (int row = 0; row < img.Height; row++)
                    {
                        for (int col = 0; col < img.Width; col++)
                        {
                            if (img.BandValuesEqual(row, col, missing))
                            {
                                mask[0, row, col] = 0;
                            }
                        }
                    } 
                }
            }

            //add borders to mask
            int border = Math.Min(mask.Height / 2, Math.Min(mask.Width / 2, DEF_MASK_BORDER));
            for (int b = 0; b < border; b++)
            {
                //whole row
                for(int col = 0; col < mask.Width; col++)
                {
                    mask[0, b, col] = 0;
                    mask[0, mask.Height - 1 - b, col] = 0;
                }

                //whole column
                for (int row = 0; row < mask.Height; row++)
                {
                    mask[0, row, b] = 0;
                    mask[0, row, mask.Width - 1 - b] = 0;
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
                return pipeline.GetDataProduct<PngDataProduct>(project, imageObs.MaskGuid).Image;
            }
            else
            {
                if (img == null)
                {
                    img = pipeline.LoadImage(imageObs.Url);
                }
                Image mask = MakeMask(pipeline, masker, roverMaskUrl, img);
                var maskProd = new PngDataProduct(mask);
                pipeline.SaveDataProduct(project, maskProd);
                imageObs.MaskGuid = maskProd.Guid;
                imageObs.Save(pipeline);
                return mask;
            }
        }
    }
}
