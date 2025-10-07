using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace JPLOPS.Imaging
{
    public static class Inpainter
    {
        /// <summary>
        /// Given an image with a mask, extend the unmasked area into the masked area by border pixels in place.
        /// If border is negative (the default) continue inpainting until there are no masked pixels left.
        /// Inpainted values are either an average of their non-masked neighbors, or a copy of some neighbor.
        /// </summary>
        /// <param name="border"></param>
        /// <param name="preserveMask">inpainting usually destroys the mask where pixels were inpainted, setting to
        /// true will preserve the original mask</param>
        public static Image Inpaint(this Image img, int border = -1, bool preserveMask = false,
                                    bool useAnyNeighbor = false, float blend = 1)
        {
            if (img.HasMask && preserveMask)
            {
                img.SaveMask();
            }

            Apply(img, border, useAnyNeighbor, blend);

            if (img.HasMask && preserveMask)
            {
                img.RestoreMask();
            }

            return img;
        }

        /// <summary>
        /// Return trues if at least one of 4 neighbors of position (r, c) in image is unmasked
        /// </summary>
        /// <param name="r"></param>
        /// <param name="c"></param>
        /// <param name="image"></param>
        /// <returns></returns>
        private static bool HasNeighbors(int r, int c, Image image)
        {
            if (r > 0 && image.IsValid(r - 1, c))
            {
                return true;
            }
            if (c > 0 && image.IsValid(r, c - 1))
            {
                return true;
            }
            if (r < image.Height - 1 && image.IsValid(r + 1, c))
            {
                return true;
            }
            if (c < image.Width - 1 && image.IsValid(r, c + 1))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Write average of up to 8 non null neighbor pixels in readImage to position (r, c) in writeImage. Does NOT modify mask
        /// </summary>
        /// <param name="r"></param>
        /// <param name="c"></param>
        /// <param name="image"></param>
        private static void FillWithNeighborAverage(int r, int c, Image image, float blend)
        {
            float num = 0;
            float[] average = new float[image.Bands];
            for (int r2 = Math.Max(0, r - 1); r2 <= Math.Min(image.Height - 1, r + 1); r2++)
            {
                for (int d2 = Math.Max(0, c - 1); d2 <= Math.Min(image.Width - 1, c + 1); d2++)
                { 
                    if (image.IsValid(r2, d2))
                    {
                        num++;
                        var bandValues = image.GetBandValues(r2, d2);
                        for (int b = 0; b < image.Bands; b++)
                        {
                            average[b] += bandValues[b];
                        }
                    }
                }
            }
            // This method should only be called when at least one neighbor is valid so num should never be zero
            float f = blend / num;
            for (int b = 0; b < image.Bands; b++)
            {
                average[b] *= f;
            }
            image.SetBandValues(r, c, average);
        }

        private static void FillWithAnyNeighbor(int r, int c, Image image, float blend)
        {
            float[] any = null;
            for (int r2 = Math.Max(0, r - 1); any == null && r2 <= Math.Min(image.Height - 1, r + 1); r2++)
            {
                for (int d2 = Math.Max(0, c - 1); any == null && d2 <= Math.Min(image.Width - 1, c + 1); d2++)
                { 
                    if (image.IsValid(r2, d2))
                    {
                        any = image.GetBandValues(r2, d2);
                    }
                }
            }
            for (int b = 0; b < image.Bands; b++)
            {
                any[b] *= blend;
            }
            // This method should only be called when at least one neighbor is valid so any should never be null
            image.SetBandValues(r, c, any);
        }

        /// <summary>
        /// Inpaint masked reagions of an image by the amount specified by pad width
        /// </summary>
        /// <param name="image"></param>
        /// <param name="padWidth"></param>
        private static void Apply(Image image, int padWidth = -1, bool useAnyNeighbor = false, float blend = 1)
        {
            if(!image.HasMask)
            {
                throw new ImageException("Image must have a mask in order to inpaint");
            }
            // in paint set up:
            //   add "edge points" to the mask of image, and store them in a new list
            List<Vector2> edgePoints = new List<Vector2>();
            List<Vector2> newEdgePoints = new List<Vector2>();
            for (int r = 0; r < image.Height; r++)
            {
                for (int c = 0; c < image.Width; c++)
                {
                    if (HasNeighbors(r, c, image) && !image.IsValid(r, c))
                    {
                        edgePoints.Add(new Vector2(r, c));
                    }
                }
            }      
            // in paint:
            //   Populate current edge points in image
            //   Use edge points to get new list of edge points and continue padding outwards
            for (int i = 0; i != padWidth; i++)
            {
                if (edgePoints.Count == 0)
                {
                    break;
                }
                foreach (Vector2 edge in edgePoints)
                {
                    if (useAnyNeighbor)
                    {
                        FillWithAnyNeighbor((int)edge.X, (int)edge.Y, image, blend);
                    }
                    else
                    {
                        FillWithNeighborAverage((int)edge.X, (int)edge.Y, image, blend);
                    }
                }   
                foreach (Vector2 edge in edgePoints)
                {
                    image.SetMaskValue((int)edge.X, (int)edge.Y, false);
                }
                newEdgePoints.Clear();
                foreach (Vector2 edge in edgePoints)
                {
                    int r = (int)edge.X;
                    int c = (int)edge.Y;

                    if (r > 0 && !image.IsValid(r - 1, c))
                    {
                        newEdgePoints.Add(new Vector2(r - 1, c));
                        image.SetMaskValue(r-1, c, false);
                    }
                    if (c > 0 && !image.IsValid(r, c - 1))
                    {
                        newEdgePoints.Add(new Vector2(r, c - 1));
                        image.SetMaskValue(r, c-1, false);
                    }
                    if (r < image.Height - 1 && !image.IsValid(r + 1, c))
                    {
                        newEdgePoints.Add(new Vector2(r + 1, c));
                        image.SetMaskValue(r+1, c, false);
                    }
                    if (c < image.Width - 1 && !image.IsValid(r, c + 1))
                    {
                        newEdgePoints.Add(new Vector2(r, c + 1));
                        image.SetMaskValue(r, c+1, false);
                    }
                }
                edgePoints = newEdgePoints.ToList();
                foreach (Vector2 edge in edgePoints)
                {
                    image.SetMaskValue((int)edge.X, (int)edge.Y, true);
                }
            }
        }
    }
}
