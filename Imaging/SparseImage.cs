using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    /// <summary>
    /// Stores an Image as array of smaller chunk Images
    /// </summary>
    public class SparseImage : Image
    {
        private Image[,] Images;
        private string baseUrl;
        private string extension;
        private int chunkSize;
        private Loader LoadImage;
        private Saver SaveImage;
        public delegate Image Loader(string url);
        public delegate void Saver(Image img, string url);

        /// <summary>
        /// Contructs a SparseImage with baseUrl and extension of chunk images to load and populate the SparseImage array as needed.
        /// </summary>
        /// <param name="bands">Number of bands in original image</param>
        /// <param name="width">Width of original image</param>
        /// <param name="height">Height of original image</param>
        /// <param name="baseUrl">Base URL of chunk images</param>
        /// <param name="extension">Extention of chunk image file (including ".")</param>
        /// <param name="chunkSize">Width and height of chunks</param>
        /// <param name="loader">Function to load chunks (default Image.Load)</param>
        /// <param name="saver">Function to save chunks (default Image.Save)</param>
        public SparseImage(int bands, int width, int height, string baseUrl, string extension, int chunkSize = 256, Loader loader = null, Saver saver = null) : base(0, 0, 0)
        {
            this.Metadata = new ImageMetadata(bands, width, height);
            this.Bands = bands;
            this.Width = width;
            this.Height = height;
            this.baseUrl = baseUrl;
            this.extension = extension;
            this.chunkSize = chunkSize;
            if (loader != null)
            {
                this.LoadImage = loader;
            }
            else
            {
                this.LoadImage = Load;
            }
            if (saver != null)
            {
                this.SaveImage = saver;
            }
            else
            {
                this.SaveImage = Save<byte>;
            }

            Images = new Image[(int)Math.Ceiling((float)Height / chunkSize), (int)Math.Ceiling((float)Width / chunkSize)];
        }

        /// <summary>
        /// Constructs a SparseImage by partitioning an Image and populating the SparseImage.
        /// </summary>
        /// <param name="largeImage">Original Image to be partitioned</param>
        /// <param name="chunkSize">Width and height of chunks</param>
        /// <param name="saver">Function to save chunks (default Image.Save)</param>
        public SparseImage(Image largeImage, int chunkSize = 256, Saver saver = null) : base(0, 0, 0)
        {
            this.Metadata = (ImageMetadata)largeImage.Metadata.Clone();
            this.Bands = largeImage.Bands;
            this.Width = largeImage.Width;
            this.Height = largeImage.Height;
            this.chunkSize = chunkSize;

            if (saver != null)
            {
                this.SaveImage = saver;
            }
            else
            {
                this.SaveImage = Save<byte>;
            }

            Images = new Image[(int)Math.Ceiling((float)Height / chunkSize), (int)Math.Ceiling((float)Width / chunkSize)];
            Partition(largeImage);
        }

        private void Save<T>(Image img, string filename)
        {
            img.Save<T>(filename);
        }

        /// <summary>
        /// Save each chunk of SparseImage separately using specified saver.
        /// </summary>
        /// <param name="baseUrl">Base URL to save chunk</param>
        /// <param name="extension">Extension for saved chunk</param>
        public void Save<T>(string baseUrl, string extension)
        {
            for (int row = 0; row < Images.GetLength(0); row++)
            {
                for (int col = 0; col < Images.GetLength(1); col++)
                {
                    if (Images[row, col] != null)
                    {
                        SaveImage(Images[row, col], CreateFileName(row, col, baseUrl, extension));
                    }
                }
            }
        }

        /// <summary>
        /// Split largeImage into chunks with dimensions chunkSize, then populate with chunks.
        /// </summary>
        /// <param name="largeImage"></param>
        private void Partition(Image largeImage)
        {
            for (int r = 0; r < largeImage.Height; r += chunkSize)
            {
                for (int c = 0; c < largeImage.Width; c += chunkSize)
                {
                    Image chunk = largeImage.Crop(r, c, Math.Min(largeImage.Width - c, chunkSize), Math.Min(largeImage.Height - r,chunkSize));
                    Images[r / chunkSize, c / chunkSize] = chunk;
                }
            }
        }

        /// <summary>
        /// Access chunk pixel corresponding to original image data at specified band, row, and column. If chunk does not exist, load it.
        /// </summary>
        /// <param name="band"></param>
        /// <param name="row"></param>
        /// <param name="column"></param>
        /// <returns></returns>
        public override float this[int band, int row, int column]
        {
            get
            {
                int rowIndex = row / chunkSize;
                int colIndex = column / chunkSize;
                if (Images[rowIndex, colIndex] == null)
                {
                    LoadChunk(rowIndex, colIndex);
                }
                return Images[rowIndex, colIndex][band, (row % chunkSize), (column % chunkSize)];

            }
            set
            {
                int rowIndex = row / chunkSize;
                int colIndex = column / chunkSize;
                if (Images[rowIndex, colIndex] == null)
                {
                    LoadChunk(rowIndex, colIndex);
                }
                Images[rowIndex, colIndex][band, (row % chunkSize), (column % chunkSize)] = value;
            }
        }

        /// <summary>
        /// Loads required chunk.
        /// </summary>
        /// <param name="rowIndex">Row index of chunk</param>
        /// <param name="colIndex">Column index of chunk</param>
        private void LoadChunk(int rowIndex, int colIndex)
        {
            Images[rowIndex, colIndex] = LoadImage(CreateFileName(rowIndex, colIndex, baseUrl, extension));
            if ((Images[rowIndex, colIndex].Height != chunkSize && rowIndex * chunkSize + Images[rowIndex, colIndex].Height != Height) 
                || (Images[rowIndex, colIndex].Width != chunkSize && colIndex * chunkSize + Images[rowIndex,colIndex].Width != Width))
            {
                throw new Exception("Chunk size does not match previously partitioned image");
            }
        }

        private string CreateFileName(int row, int col, string baseUrl, string extension)
        {
            return baseUrl + "_" + row + "_" + col + "_" + extension;
        }
    }
}