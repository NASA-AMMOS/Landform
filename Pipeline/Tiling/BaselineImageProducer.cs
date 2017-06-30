using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;
using OPS.MathExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    /// <summary>
    /// Image producer that can produce images for tiles from a baseline mesh image
    /// </summary>
    public class BaselineImageProducer : IImageProducer
    {
        Image fullImage;
        int minCropResolution;
        int maxPixels;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fullImage">Original image used in the creation of a baseline mesh</param>
        /// <param name="minCropResolution">The minimum dimension an image tile can be (neither X or Y dimension can be below this)</param>
        /// <param name="maxPixels">Max number of pixels a returend image can be (i.e. max value of width*height).  Set to 0 to disable resizing, images will still be resized up to nearest power of 2.</param>
        public BaselineImageProducer(Image fullImage, int minCropResolution, int maxPixels)
        {
            this.fullImage = fullImage;
            this.minCropResolution = minCropResolution;
            this.maxPixels = maxPixels;
        }

        /// <summary>
        /// Generate an image for the provided mesh.  Typically mesh will be a subset or decimated version of the original baseline mesh 
        /// As a side effect the UVs of the provided mesh will be modified to match the returned image
        /// </summary>
        /// <param name="mesh">Mesh with uvs for which to generate an Image.  UVs will be modified as a side effect</param>
        /// <returns></returns>
        public Image GenerateImage(Mesh mesh)
        {
            // Find the area of pixels in the source image covered by this mesh
            var pixelBounds = fullImage.UVBoundsToPixel(mesh.UVBounds());

            // Compute the starting position
            var start = pixelBounds.Min;
            start.X = (int)start.X;
            start.Y = (int)start.Y;
            // Adjust the starting position and size of our bounding box if it is less then min resolution
            var center = pixelBounds.Center();
            var pixelSize = pixelBounds.Size();
            if (pixelSize.X < minCropResolution)
            {
                start.X = Math.Max(0, Math.Floor(center.X - (minCropResolution / 2)));
                pixelSize.X = minCropResolution;
            }
            if (pixelSize.Y < minCropResolution)
            {
                start.Y = Math.Max(0, Math.Floor(center.Y - (minCropResolution / 2)));
                pixelSize.Y = minCropResolution;
            }
            pixelSize.X = (int)Math.Ceiling(pixelSize.X);
            pixelSize.Y = (int)Math.Ceiling(pixelSize.Y);
            // Constrain size to the size of the image
            if (start.X + pixelSize.X >= fullImage.Width)
            {
                pixelSize.X = fullImage.Width - start.X - 1;
            }
            if (start.Y + pixelSize.Y >= fullImage.Height)
            {
                pixelSize.Y = fullImage.Height - start.Y - 1;
            }
            // Create a cropped version of the image that only covers the area related to our mesh
            var croppedImage = fullImage.Crop((int)start.Y, (int)start.X, (int)pixelSize.X, (int)pixelSize.Y);
            // Re-uv the mesh to match the provided image
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Vector2 beforePixel = fullImage.UVToPixel(mesh.Vertices[i].UV);
                Vector2 afterPixel = new Vector2(beforePixel.X - start.X, beforePixel.Y - start.Y);
                mesh.Vertices[i].UV = croppedImage.PixelToUV(afterPixel);
            }
            // Round up size to nearest power of 2
            var targetWidth = MathE.CeilPowerOf2(pixelSize.X);
            var targetHeight = MathE.CeilPowerOf2(pixelSize.Y);
            // Is image resizing enabled?
            if (maxPixels != 0)
            {
                // Divide the largest dimensio of the image in half until total pixels is less than limit
                while (targetWidth * targetHeight > this.maxPixels && targetWidth > minCropResolution && targetHeight > minCropResolution)
                {
                    if (targetWidth > targetHeight)
                    {
                        targetWidth /= 2;
                    }
                    else
                    {
                        targetHeight /= 2;
                    }
                }
            }
            // Resize image
            croppedImage = croppedImage.ResizeSimpleBicubic(targetWidth, targetHeight);
            return croppedImage;
        }
    }
}
