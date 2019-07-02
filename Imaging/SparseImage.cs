using Microsoft.Xna.Framework;
using OPS.Util;
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
        private bool useCache;
        private bool isNewImage;
        private bool diskBack;

        //Used if largeImage can fit into memory for partitioning
        private Image[,] Images;

        //For limiting entire memory footprint
        private IImageConverter converter = null;
        protected LRUCache<Vector2, Image> ChunkCache;

        protected string baseUrl;
        protected string extension;
        protected int chunkSize;

        private string Filename {
            get { return baseUrl + extension; }            
        }

        private Action<Image, Vector2, string> getSaveFunction()
        {
            return new Action<Image, Vector2, string>((img, rc, path) =>
            {
                SaveChunk<Byte>(img, CreateFileName((int)rc.X, (int)rc.Y, path, extension));
            });
        }

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
public SparseImage(int bands, int width, int height, string baseUrl, string extension, int chunkSize = 256, bool useCache = false, bool diskBack = false, int cacheSize = 5000) : base(0, 0, 0)
        {
            this.isNewImage = true;
            this.useCache = useCache;
            this.diskBack = diskBack;
            this.Metadata = new ImageMetadata(bands, width, height);
            this.Bands = bands;
            this.Width = width;
            this.Height = height;
            this.baseUrl = baseUrl;
            this.extension = extension;
            this.chunkSize = chunkSize;
            if (useCache)
            {
                ChunkCache = new LRUCache<Vector2, Image>(cacheSize, Path.GetDirectoryName(Filename), diskBack ? getSaveFunction() : null);
            }
            else
            {
                Images = new Image[(int)Math.Ceiling((float)Height / chunkSize), (int)Math.Ceiling((float)Width / chunkSize)];
            }
        }

        /// <summary>
        /// Constructs a SparseImage by partitioning an Image and populating the SparseImage.
        /// </summary>
        /// <param name="largeImage">Original Image to be partitioned</param>
        /// <param name="chunkSize">Width and height of chunks</param>
        /// <param name="saver">Function to save chunks (default Image.Save)</param>
        public SparseImage(Image largeImage, int chunkSize = 256) : base(0, 0, 0)
        {
            this.useCache = false;
            this.isNewImage = false;
            this.Metadata = (ImageMetadata)largeImage.Metadata.Clone();
            this.Bands = largeImage.Bands;
            this.Width = largeImage.Width;
            this.Height = largeImage.Height;
            this.Metadata = new ImageMetadata(Bands, Width, Height);
            this.chunkSize = chunkSize;

            Images = new Image[(int)Math.Ceiling((float)Height / chunkSize), (int)Math.Ceiling((float)Width / chunkSize)];
            Partition(largeImage);
        }

        /// <summary>
        /// Constructs a SparseImage for image on disk without populating chunks.
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="chunkSize"></param>
        public SparseImage(string filename, IImageConverter converter, int chunkSize = 1024, bool useCache = false, bool diskBack = false, int cacheSize = 10000, bool initializeChunksOnDisk = false) : base(0,0,0)
        {
            this.chunkSize = chunkSize;
            this.converter = converter;
            this.useCache = useCache;
            this.diskBack = diskBack;
            this.baseUrl = Path.Combine(Path.GetDirectoryName(filename), Path.GetFileNameWithoutExtension(filename));
            this.extension = Path.GetExtension(filename);
            this.isNewImage = false;

            ImageSerializer s = ImageSerializers.Instance.GetSerializer(extension);
            if (s.GetType() != typeof(GDALSerializer))
            {
                throw new NotImplementedException("Partial image read only supported for GDALSerializer.");
            }
            ((GDALSerializer)s).GetMetadata(filename, out Bands, out Width, out Height);
            this.Metadata = new ImageMetadata(Bands, Width, Height);

            if (useCache)
            {
                this.ChunkCache = new LRUCache<Vector2, Image>(cacheSize, Path.GetDirectoryName(filename), diskBack ? getSaveFunction() : null);
            } else
            {
                Images = new Image[(int)Math.Ceiling((float)Height / chunkSize), (int)Math.Ceiling((float)Width / chunkSize)];
            }

            if ((!useCache || diskBack) && initializeChunksOnDisk)
            {
                Partition(filename);
            }
        }

        protected virtual void SaveChunk<T>(Image img, string filename)
        {
            img.Save<T>(filename);
        }

        /// <summary>
        /// Loads required chunk.
        /// </summary>
        /// <param name="rowIndex">Row index of chunk</param>
        /// <param name="colIndex">Column index of chunk</param>
        protected virtual Image LoadChunk(string filename)
        {
            return Image.Load(filename);
        }


        /// <summary>
        /// Save each chunk of SparseImage separately using specified saver.
        /// </summary>
        /// <param name="baseUrl">Base URL to save chunk</param>
        /// <param name="extension">Extension for saved chunk</param>
        public void Save<T>(string baseUrl, string extension)
        {
            if(useCache)
            {
                //TODO: implement this if needed
                throw new NotImplementedException("Saving cached sparse image not supported.");
            }
            for (int row = 0; row < Images.GetLength(0); row++)
            {
                for (int col = 0; col < Images.GetLength(1); col++)
                {
                    if (Images[row, col] != null)
                    {
                        SaveChunk<T>(Images[row, col], CreateFileName(row, col, baseUrl, extension));
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
        /// Split an image into chunks with dimensions chunkSize without loading the full image into memory; chunks stored on disk for on demand loading.
        /// </summary>
        private void Partition(string filename)
        {
            ImageSerializer s = ImageSerializers.Instance.GetSerializer(extension);
            if (s.GetType() != typeof(GDALSerializer))
            {
                throw new NotImplementedException("Partial image read only supported for GDALSerializer.");
            }
            for (int r = 0; r <= Height / chunkSize; r++)
            {
                for (int c = 0; c <= Width / chunkSize; c++)
                {
                    Image chunk = ((GDALSerializer)s).PartialRead(filename, c * chunkSize, r * chunkSize, Math.Min(Width - c * chunkSize, chunkSize), Math.Min(Height - r * chunkSize, chunkSize), converter);
                    SaveChunk<byte>(chunk, CreateFileName(r, c, filename, extension));
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
                EnsureChunkLoaded(rowIndex, colIndex);
                Image chunk;
                if (useCache)
                {
                    chunk = ChunkCache[new Vector2(rowIndex, colIndex)];
                }
                else
                {
                    chunk = Images[rowIndex, colIndex];
                }
                return chunk[band, (row % chunkSize), (column % chunkSize)];
            }
            set
            {
                int rowIndex = row / chunkSize;
                int colIndex = column / chunkSize;
                EnsureChunkLoaded(rowIndex, colIndex);
                Image chunk;
                if (useCache)
                {
                    chunk = ChunkCache[new Vector2(rowIndex, colIndex)];

                } else {
                    chunk = Images[rowIndex, colIndex];
                }
                chunk[band, (row % chunkSize), (column % chunkSize)] = value;
            }
        }

        void EnsureChunkLoaded(int rowIndex, int colIndex)
        {
            string chunkName = CreateFileName(rowIndex, colIndex, baseUrl, extension);
            Image chunk = null;
            if (useCache)
            {
                Vector2 key = new Vector2(rowIndex, colIndex);
                if (!ChunkCache.ContainsKey(key))
                {
                    
                    if (diskBack && File.Exists(chunkName))
                    {
                        chunk = LoadChunk(chunkName);
                    }
                    else if (isNewImage)
                    {   
                        chunk = new Image(Bands, Math.Min(Width - colIndex * chunkSize, chunkSize), Math.Min(Height - rowIndex * chunkSize, chunkSize));
                    }
                    else
                    {
                        ImageSerializer s = ImageSerializers.Instance.GetSerializer(extension);
                        if (s.GetType() != typeof(GDALSerializer))
                        {
                            throw new NotImplementedException("Partial image read only supported for GDALSerializer.");
                        }
                        chunk = ((GDALSerializer)s).PartialRead(Filename, colIndex * chunkSize, rowIndex * chunkSize, Math.Min(Width - colIndex * chunkSize, chunkSize), Math.Min(Height - rowIndex * chunkSize, chunkSize), converter != null ? converter : s.DefaultReadConverter());
                    }
                    ChunkCache.Add(key, chunk);
                } else
                {
                    chunk = ChunkCache[key];
                }
            }
            else
            {
                if (Images[rowIndex, colIndex] == null)
                {
                    if(File.Exists(chunkName))
                    {
                        chunk = LoadChunk(chunkName);
                    } else
                    {
                        ImageSerializer s = ImageSerializers.Instance.GetSerializer(extension);
                        if (s.GetType() != typeof(GDALSerializer))
                        {
                            throw new NotImplementedException("Partial image read only supported for GDALSerializer.");
                        }
                        chunk = ((GDALSerializer)s).PartialRead(Filename, colIndex * chunkSize, rowIndex * chunkSize, Math.Min(Width - colIndex * chunkSize, chunkSize), Math.Min(Height - rowIndex * chunkSize, chunkSize), converter != null ? converter : s.DefaultReadConverter());
                    }           
                    Images[rowIndex, colIndex] = chunk;
                } else
                {
                    chunk = Images[rowIndex, colIndex];
                }
            }
            if ((chunk.Height != chunkSize && rowIndex * chunkSize + chunk.Height != Height) ||
                        (chunk.Width != chunkSize && colIndex * chunkSize + chunk.Width != Width) ||
                        (chunk.Bands != this.Bands))
            {
                throw new Exception("Chunk size does not match previously partitioned image");
            }
        }

        protected string CreateFileName(int row, int col, string baseUrl, string extension)
        {
            return baseUrl + "_" + row + "_" + col + extension;
        }
    }
}