using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using OPS.Imaging;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{

    public static class InPainter
    {
        /// <summary>
        /// Return trues if at least one of 4 neighbors of position (r, c) in image is unmasked
        /// </summary>
        /// <param name="r"></param>
        /// <param name="c"></param>
        /// <param name="image"></param>
        /// <returns></returns>
        static bool HasNeighbors(int r, int c, Image image)
        {
            if (r > 0 && image.IsValid(r - 1, c))
                return true;
            if (c > 0 && image.IsValid(r, c - 1))
                return true;
            if (r < image.Height - 1 && image.IsValid(r + 1, c))
                return true;
            if (c < image.Width - 1 && image.IsValid(r, c + 1))
                return true;
            return false;
        }

        /// <summary>
        /// Write average of up to 8 non null neighbor pixels in readImage to position (r, c) in writeImage 
        /// </summary>
        /// <param name="r"></param>
        /// <param name="c"></param>
        /// <param name="readImage"></param>
        /// <param name="writeImage"></param>
        static void Pad(int r, int c, Image readImage, Image writeImage)
        {
            float num = 0;
            float[] average = new float[readImage.Bands];
            for (int b = 0; b < readImage.Bands; b++)
                average[b] = 0;
            if (r > 0 && readImage.IsValid(r - 1, c))
            {
                num++;
                for (int b = 0; b < readImage.Bands; b++)
                    average[b] += readImage.GetBandValues(r - 1, c)[b];
            }
            if (c > 0 && readImage.IsValid(r, c - 1))
            {
                num++;
                for (int b = 0; b < readImage.Bands; b++)
                    average[b] += readImage.GetBandValues(r, c - 1)[b];
            }
            if (r < readImage.Height - 1 && readImage.IsValid(r + 1, c))
            {
                num++;
                for (int b = 0; b < readImage.Bands; b++)
                    average[b] += readImage.GetBandValues(r + 1, c)[b];
            }
            if (c < readImage.Width - 1 && readImage.IsValid(r, c + 1))
            {
                num++;
                for (int b = 0; b < readImage.Bands; b++)
                    average[b] += readImage.GetBandValues(r, c + 1)[b];
            }

            if (r > 0 && c > 0 && readImage.IsValid(r - 1, c - 1))
            {
                num++;
                for (int b = 0; b < readImage.Bands; b++)
                    average[b] += readImage.GetBandValues(r - 1, c - 1)[b];
            }
            if (r > 0 && c < readImage.Width - 1 && readImage.IsValid(r - 1, c + 1))
            {
                num++;
                for (int b = 0; b < readImage.Bands; b++)
                    average[b] += readImage.GetBandValues(r - 1, c + 1)[b];
            }
            if (r < readImage.Height - 1 && c > 0 && readImage.IsValid(r + 1, c - 1))
            {
                num++;
                for (int b = 0; b < readImage.Bands; b++)
                    average[b] += readImage.GetBandValues(r + 1, c - 1)[b];
            }

            if (r < readImage.Height - 1 && c < readImage.Width - 1 && readImage.IsValid(r + 1, c + 1))
            {
                num++;
                for (int b = 0; b < readImage.Bands; b++)
                    average[b] += readImage.GetBandValues(r + 1, c + 1)[b];
            }

            for (int b = 0; b < readImage.Bands; b++)
                average[b] /= num;
            writeImage.SetBandValues(r, c, average);
        }

        public static void InPaint(Image image, int padWidth = -1)
        {
            // in paint set up:
            //   create copy of image
            //   add "edge points" to the mask of image, and store them in a new list
            Image imageCopy = (Image)image.Clone();

            List<Vector2> edgePoints = new List<Vector2>();
            List<Vector2> newEdgePoints;

            for (int r = 0; r < image.Height; r++)
            {
                for (int c = 0; c < image.Width; c++)
                {
                    if (HasNeighbors(r, c, imageCopy) && imageCopy.IsInvalid(r, c))
                    {
                        image.SetMaskValue(r, c, false);
                        edgePoints.Add(new Vector2(r, c));
                    }
                }
            }

            // in paint:
            //   Use copy to populate current edge points in destImage
            //   Use edge points to get new list of edge points and continue padding outwards
            for (int i = 0; i != padWidth; i++)
            {
                if (edgePoints.Count == 0)
                    break;
                foreach (Vector2 edge in edgePoints)
                {
                    Pad((int)edge.X, (int)edge.Y, imageCopy, image);
                }
                imageCopy = (Image)image.Clone();
                newEdgePoints = new List<Vector2>();
                foreach (Vector2 edge in edgePoints)
                {
                    int r = (int)edge.X;
                    int c = (int)edge.Y;
                    if (r > 0 && image.IsInvalid(r - 1, c))
                    {
                        image.SetMaskValue(r - 1, c, false);
                        newEdgePoints.Add(new Vector2(r - 1, c));
                    }
                    if (c > 0 && image.IsInvalid(r, c - 1))
                    {
                        image.SetMaskValue(r, c - 1, false);
                        newEdgePoints.Add(new Vector2(r, c - 1));
                    }
                    if (r < image.Height - 1 && image.IsInvalid(r + 1, c))
                    {
                        image.SetMaskValue(r + 1, c, false);
                        newEdgePoints.Add(new Vector2(r + 1, c));
                    }
                    if (c < image.Width - 1 && image.IsInvalid(r, c + 1))
                    {
                        image.SetMaskValue(r, c + 1, false);
                        newEdgePoints.Add(new Vector2(r, c + 1));
                    }
                }
                edgePoints = newEdgePoints;
            }
        }
    }
}
