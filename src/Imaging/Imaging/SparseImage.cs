using Microsoft.Xna.Framework;
using JPLOPS.Util;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;

namespace JPLOPS.Imaging
{
    /// <summary>
    /// Stores an Image as array of smaller chunk Images
    /// </summary>
    public class SparseImage : Image
    {
        protected Image[,] chunks;

        //alternate instead of chunks array for limiting entire memory footprint
        protected LRUCache<Vector2, Image> chunkCache;

        protected ConcurrentDictionary<Pixel, Object> chunkLocks = new ConcurrentDictionary<Pixel, Object>();

        //persisted backing, if any
        protected string basePath;
        protected string extension;

        //large image backing, if any
        protected Image largeImage;

        //chunk images are chunkSize x chunkSize
        //except those on the right and bottom borders may be smaller
        protected int chunkSize;

        private int chunkRows;
        private int chunkCols;

        private bool hasSavedMask;
        private bool initialMaskValue;
        private bool _hasMask;
        public override bool HasMask { get { return _hasMask; } }

        /// <summary>
        /// Construct a new empty sparse image.
        ///
        /// Chunks are allocated in memory lazily.
        ///
        /// The sparse image has no persistence other than possibly temporary disk backing for the chunk cache.
        /// </summary>
        /// <param name="bands">number of bands</param>
        /// <param name="width">total image width</param>
        /// <param name="height">total image height</param>
        /// <param name="chunkSize">width and height of chunks</param>
        /// <param name="cacheSize">if > 0 then use LRU cache of chunks instead of full array</param>
        /// <param name="diskBackedCache">enable disk backing for cache</param>
        public SparseImage(int bands, int width, int height, int chunkSize = 256, int cacheSize = 0,
                           bool diskBackedCache = false)
            : base(0, 0, 0) //don't let base class constructor allocate image buffer
        {
            this.Bands = bands;
            this.Width = width;
            this.Height = height;
            this.Metadata = new ImageMetadata(bands, width, height);
            this.chunkSize = chunkSize;
            InitChunkCacheOrArray(cacheSize, diskBackedCache);
        }

        /// <summary>
        /// Construct a sparse image by backed by an existing image.
        ///
        /// Chunks are allocated in memory lazily.
        /// Call Populate() to load them all and release the reference to the existing image.
        ///
        /// The sparse image has no persistence other than possibly temporary disk backing for the chunk cache.
        /// </summary>
        /// <param name="largeImage">original image</param>
        /// <param name="chunkSize">width and height of chunks</param>
        /// <param name="cacheSize">if > 0 then use LRU cache of chunks instead of full array</param>
        /// <param name="diskBackedCache">enable disk backing for cache</param>
        public SparseImage(Image largeImage, int chunkSize = 256, int cacheSize = 0, bool diskBackedCache = false)
            : base(0, 0, 0) //don't let base class constructor allocate image buffer
        {
            this.Bands = largeImage.Bands;
            this.Width = largeImage.Width;
            this.Height = largeImage.Height;
            if (largeImage.Metadata != null)
            {
                this.Metadata = (ImageMetadata)largeImage.Metadata.Clone();
            }
            else
            {
                this.Metadata = new ImageMetadata(Bands, Width, Height);
            }
            if (largeImage.CameraModel != null)
            {
                this.CameraModel = (CameraModel)largeImage.CameraModel.Clone();
            }
            this._hasMask = largeImage.HasMask;
            this.chunkSize = chunkSize;
            this.largeImage = largeImage;
            InitChunkCacheOrArray(cacheSize, diskBackedCache);
        }

        /// <summary>
        /// Create a sparse image backed by persisted storage.
        ///
        /// The persisted backing may either
        /// (a) not exist yet
        /// (b) be a full image at basePath+extension
        /// (c) be one or more chunk files at basePath_ROW_COL+extension
        ///
        /// The storage is not loaded immediately but lazily as needed.  Call Populate() to load them all.
        ///
        /// When a chunk needs to be loaded
        /// (1) if it already has been persisted as an independent chunk that is loaded
        /// (2) therwise if the full image has been persisted the chunk is loaded out of it with PartialRead()
        /// (3) otherwise a new blank chunk is created
        ///
        /// When a chunk needs to be persisted it is saved as an independent chunk file, never the full image.
        ///
        /// Override IsPersisted(), SaveChunk(), LoadChunk(), and PartialReadFile() to customize persistence.
        /// The default is local disk treating basePath as a file path.
        ///
        /// </summary>
        /// <param name="bands">number of bands</param>
        /// <param name="width">total image width</param>
        /// <param name="height">total image height</param>
        /// <param name="basePath">base path for persistence</param>
        /// <param name="extension">filename extension for persistence including "."</param>
        /// <param name="chunkSize">width and height of chunks</param>
        /// <param name="cacheSize">if > 0 then use LRU cache of chunks instead of full array</param>
        /// <param name="diskBackedCache">enable disk backing for cache</param>
        public SparseImage(int bands, int width, int height, string basePath, string extension, int chunkSize = 256,
                           int cacheSize = 0, bool diskBackedCache = false)
            : base(0, 0, 0) //don't let base class constructor allocate image buffer
        {
            this.Bands = bands;
            this.Width = width;
            this.Height = height;
            this.Metadata = new ImageMetadata(bands, width, height);
            this.basePath = basePath;
            this.extension = extension;
            this.chunkSize = chunkSize;
            InitChunkCacheOrArray(cacheSize, diskBackedCache);
        }

        /// <summary>
        /// Create a sparse image backed by persisted storage.
        ///
        /// The chunks are loaded lazily with PartialRead().  Call Populate() to load them all.
        ///
        /// The chunks are persisted on Save() as independent chunks at basePath_ROW_COL+extension.
        ///
        /// Override IsPersisted(), SaveChunk(), LoadChunk(), and PartialReadFile() to customize persistence.
        /// The default is local disk treating basePath as a file path.
        ///
        /// </summary>
        /// <param name="largeImagePath">path to large image</param>
        /// <param name="chunkSize">width and height of chunks</param>
        /// <param name="cacheSize">if > 0 then use LRU cache of chunks instead of full array</param>
        /// <param name="diskBackedCache">enable disk backing for cache</param>
        public SparseImage(string largeImagePath, int chunkSize = 256, int cacheSize = 0, bool diskBackedCache = false)
            : base(0, 0, 0) //don't let base class constructor allocate image buffer
        {
            InitFromLargeImage(largeImagePath, chunkSize, cacheSize, diskBackedCache);
        }

        //for subclassing
        protected SparseImage() : base(0, 0, 0) //don't let base class constructor allocate image buffer
        {
        }

        public SparseImage(SparseImage that)
            : this(that.Bands, that.Width, that.Height, that.basePath, that.extension, that.chunkSize)
        {
            if (that.Metadata != null)
            {
                this.Metadata = (ImageMetadata)that.Metadata.Clone();
            }

            if (that.CameraModel != null)
            {
                this.CameraModel = (CameraModel)that.CameraModel.Clone();
            }

            _hasMask = that.HasMask;

            InitChunkCacheOrArray(that.chunkCache != null ? that.chunkCache.Capacity : 0,
                                  that.chunkCache != null ? that.chunkCache.DiskBacked : false);

            for (int r = 0; r < chunkRows; r++)
            {
                for (int c = 0; c < chunkCols; c++)
                {
                    Image chunk = that.GetExistingChunk(r, c);
                    if (chunk != null)
                    {
                        chunk = (Image)chunk.Clone();
                        if (chunks != null)
                        {
                            chunks[r, c] = chunk;
                        } 
                        else
                        {
                            chunkCache[new Vector2(r, c)] = chunk;
                        }
                    }
                }
            }
        }

        protected override void CopyDataTo<TT>(GenericImage<TT> that)
        {
            if (!(typeof(TT).IsAssignableFrom(typeof(float))))
            {
                throw new ArgumentException("failed to copy sparse image data: type mismatch");
            }

            if (that.Bands != Bands || that.Width != Width || that.Height != Height)
            {
                throw new ArgumentException("failed to copy sparse image data: size mismatch");
            }

            for (int b = 0; b < Bands; b++)
            {
                for (int r = 0; r < Height; r++)
                {
                    for (int c = 0; c < Width; c++)
                    {
                        that[b, r, c] = (TT)Convert.ChangeType(this[b, r, c], typeof(TT));
                    }
                }
            }
        }

        protected override void CopyMaskTo<TT>(GenericImage<TT> that)
        {
            if (!HasMask || !that.HasMask || that.Width != Width || that.Height != Height)
            {
                throw new ArgumentException("failed to copy sparse image mask");
            }
            for (int r = 0; r < Height; r++)
            {
                for (int c = 0; c < Width; c++)
                {
                    that.SetMaskValue(r, c, !IsValid(r, c));
                }
            }
        }

        /// <summary>
        /// Performas a deep copy of the image
        /// </summary>
        /// <returns></returns>
        public override object Clone()
        {
            return new SparseImage(this);
        }

        public override Image Instantiate(int bands, int width, int height)
        {
            return new SparseImage(bands, width, height, basePath, extension, chunkSize);
        }

        //broken out to facilitate subclassing where GetImageMetadataForPartialRead() may need prior init
        protected void InitFromLargeImage(string largeImagePath, int chunkSize, int cacheSize, bool diskBackedCache)
        {
            this.chunkSize = chunkSize;
            this.basePath = StringHelper.StripUrlExtension(largeImagePath);
            this.extension = StringHelper.GetUrlExtension(largeImagePath);
            int bands;
            int width;
            int height;
            GetImageMetadataForPartialRead(largeImagePath, out bands, out width, out height);
            this.Bands = bands;
            this.Width = width;
            this.Height = height;
            this.Metadata = new ImageMetadata(Bands, Width, Height);
            InitChunkCacheOrArray(cacheSize, diskBackedCache);
        }

        protected void InitChunkCacheOrArray(int cacheSize, bool diskBackedCache)
        {
            chunkRows = (int)Math.Ceiling((float)Height / chunkSize);
            chunkCols = (int)Math.Ceiling((float)Width / chunkSize);
            if (cacheSize > 0)
            {
                if (diskBackedCache)
                {
                    var wc = GetWriteConverter();
                    var rc = GetReadConverter();
                    chunkCache =
                        new LRUCache<Vector2, Image>
                        (cacheSize,
                         keyToFilename: key => ChunkPath((int)key.X, (int)key.Y, "chunk", extension),
                         save: (fn, img) => SaveCacheChunk(img, fn, wc),
                         load: fn => rc != null ? Image.Load(fn, rc) : Image.Load(fn));
                }
                else
                {
                    chunkCache = new LRUCache<Vector2, Image>(cacheSize);
                }
            }
            else
            {
                chunks = new Image[chunkRows, chunkCols];
            }
        }

        public override BinaryImage InstantiateBinaryImage(int width, int height)
        {
            return new SparseBinaryImage(width, height, chunkSize);
        }

        public bool CanDensify()
        {
            return string.IsNullOrEmpty(CheckSize<float>(Bands, Width, Height));
        }

        public Image Densify()
        {
            string err = Image.CheckSize(Bands, Width, Height);
            if (!string.IsNullOrEmpty(err))
            {
                throw new InvalidOperationException(err);
            }
            return new Image(this);
        }

        /// <summary>
        /// Subclasses can override this to spew progress for long operations.
        /// </summary>
        protected virtual void Progress(string msg, params Object[] args)
        {
        }

        /// <summary>
        /// check if an image is persisted
        /// this could be either the full large image or a chunk image
        /// </summary>
        protected virtual bool IsPersisted(string path)
        {
            return File.Exists(path);
        }

        /// <summary>
        /// persist a chunk image to disk cache  
        /// default implementation saves as float
        /// </summary>
        protected virtual void SaveCacheChunk(Image img, string path, IImageConverter writeConverter)
        {
            if (writeConverter != null)
            {
                img.Save<float>(path, writeConverter);
            }
            else
            {
                img.Save<float>(path);
            }
        }

        /// <summary>
        /// persist a chunk image
        /// </summary>
        protected virtual void SaveChunk<T>(Image img, string path)
        {
            IImageConverter conv = GetWriteConverter();
            if (conv != null)
            {
                img.Save<T>(path, conv);
            }
            else
            {
                img.Save<T>(path);
            }
        }

        /// <summary>
        /// unpersist a chunk image
        /// </summary>
        protected virtual Image LoadChunk(string path)
        {
            IImageConverter conv = GetReadConverter();
            if (conv != null)
            {
                return Image.Load(path, conv);
            }
            else
            {
                return Image.Load(path);
            }
        }

        /// <summary>
        /// persist all chunks
        /// </summary>
        /// <param name="basePath">base path to save chunks (overrides value provided to constructor)</param>
        /// <param name="extension">extension for saved chunks (overrides value provided to constructor)</param>
        public void SaveAllChunks<T>(string basePath = null, string extension = null)
        {
            basePath = basePath ?? this.basePath;
            extension = extension ?? this.extension;
            if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(extension))
            {
                throw new ArgumentException("must specify base path and extension to save sparse image");
            }
            int vChunks = (int)Math.Ceiling(((float)Height) / chunkSize);
            int hChunks = (int)Math.Ceiling(((float)Width) / chunkSize);
            int n = 0;
            for (int r = 0; r < vChunks; r++) 
            {
                for (int c = 0; c < hChunks; c++)
                {
                    SaveChunk<T>(GetChunk(r, c), ChunkPath(r, c, basePath, extension));
                    Progress("saved chunk ({0},{1}), {2}/{3} complete", r, c, ++n, vChunks * hChunks);
                }
            }
        }

        /// <summary>
        /// Populate all chunks.
        ///
        /// If this sparse image is backed by a large in-memory image then the chunks are cropped out of it.
        /// NOTE: if this is combined with LRU caching of the chunks without disk backing for the LRU cache then if
        /// the cache is not large enough some chunks will be ejected from the cache immediately.
        ///
        /// If this sparse image is backed by a large persisted image and/or individual chunk images, they are
        /// unpersisted.
        /// </summary>
        /// <param name="releaseBacking">whether to release the reference to the backing image, if any</param>
        public void Populate(bool releaseBacking = true)
        {
            int vChunks = (int)Math.Ceiling(((float)Height) / chunkSize);
            int hChunks = (int)Math.Ceiling(((float)Width) / chunkSize);
            int n = 0;
            for (int r = 0; r < vChunks; r++)
            {
                for (int c = 0; c < hChunks; c++)
                {
                    GetChunk(r, c);
                    Progress("populated chunk ({0},{1}), {2}/{3} complete", r, c, ++n, vChunks * hChunks);
                }
            }
            if (releaseBacking)
            {
                largeImage = null;
            }
        }

        protected virtual IImageConverter GetReadConverter()
        {
            return null;
        }

        protected virtual IImageConverter GetWriteConverter()
        {
            return null;
        }

        protected virtual string ChunkPath(int row, int col, string path, string extension)
        {
            return string.Format("{0}_{1}_{2}{3}", path, row, col, extension);
        }

        /// <summary>
        /// Either get a new instance of an image serializer for extension and check that it's capable of partial read.
        /// Or check that the passed serializer is capable of partial read.
        /// </summary>
        protected virtual ImageSerializer GetOrCheckPartialReadSerializer(ImageSerializer s = null)
        {
            if (s == null)
            {
                s = ImageSerializers.Instance.GetSerializer(extension);
            }
            if (s.GetType() != typeof(GDALSerializer))
            {
                throw new NotImplementedException("Partial image read only supported for GDALSerializer.");
            }
            return s;
        }

        /// <summary>
        /// indirection to transform the pathname for the file to be used for partial reads
        /// </summary>
        protected virtual string PartialReadFile(string path)
        {
            return path;
        }

        /// <summary>
        /// Read image metadata without reading the whole image into memory.
        /// </summary>
        protected virtual void GetImageMetadataForPartialRead(string path, out int bands, out int width, out int height,
                                                              ImageSerializer s = null)
        {
            s = GetOrCheckPartialReadSerializer(s);
            ((GDALSerializer)s).GetMetadata(PartialReadFile(path), out bands, out width, out height);
        }

        /// <summary>
        /// Read a chunk of an image without reading the whole image into memory.
        /// </summary>
        protected virtual Image PartialRead(string path, int chunkRow, int chunkCol, ImageSerializer s = null)
        {
            s = GetOrCheckPartialReadSerializer(s);
            int x = chunkCol * chunkSize;
            int y = chunkRow * chunkSize;
            int w = Math.Min(x + chunkSize, Width) - x;
            int h = Math.Min(y + chunkSize, Height) - y;
            return ((GDALSerializer)s).PartialRead(PartialReadFile(path), x, y, w, h, GetReadConverter());
        }

        /// <summary>
        /// Access chunk pixel corresponding to original image data at specified band, row, and column.
        /// </summary>
        /// <param name="band"></param>
        /// <param name="row"></param>
        /// <param name="column"></param>
        /// <returns></returns>
        public override float this[int band, int row, int column]
        {
            get
            {
                return GetChunk(row / chunkSize, column / chunkSize)[band, (row % chunkSize), (column % chunkSize)];
            }
            set
            {
                GetChunk(row / chunkSize, column / chunkSize)[band, (row % chunkSize), (column % chunkSize)] = value;
            }
        }

        /// <summary>
        /// If the chunk is already in memory (or the chunkCache disk cache) then just return it.
        /// Otherwise unpersist the chunk if we have persisted backing.
        /// Otherwise allocate a new blank chunk.
        /// </summary>
        protected Image GetChunk(int chunkRow, int chunkCol)
        {
            Image chunk = null;

            //only allow one thread at a time in here
            //this avoids multiple threads simultaneously loading the same chunk
            Object chunkLock = chunkLocks.GetOrAdd(new Pixel(chunkRow, chunkCol), _ => new Object());
            lock (chunkLock) {

                //fast path: see if it's already in memory
                if (chunks != null)
                {
                    chunk = chunks[chunkRow, chunkCol];
                }
                else
                {
                    chunk = chunkCache[new Vector2(chunkRow, chunkCol)];
                }
                
                if (chunk == null)
                {
                    //slow path: load or allocate the chunk
                    
                    int w = Math.Min(Width - chunkCol * chunkSize, chunkSize);
                    int h = Math.Min(Height - chunkRow * chunkSize, chunkSize);
                    
                    if (largeImage != null)
                    {
                        //if we are backed by a large monolithic image then crop out the chunk
                        chunk = largeImage.Crop(chunkRow * chunkSize, chunkCol * chunkSize, w, h);
                    }
                    else if (!string.IsNullOrEmpty(basePath) && !string.IsNullOrEmpty(extension))
                    {
                        //otherwise if we have persistence backing then try to load the chunk
                        string chunkFile = ChunkPath(chunkRow, chunkCol, basePath, extension);
                        string largeImageFile = basePath + extension;
                        if (IsPersisted(chunkFile))
                        {
                            chunk = LoadChunk(chunkFile);
                        }
                        else if (IsPersisted(largeImageFile))
                        {
                            chunk = PartialRead(largeImageFile, chunkRow, chunkCol);
                        }
                        
                        if (chunk != null && (chunk.Bands != Bands || chunk.Width != w || chunk.Height != h))
                        {
                            throw new Exception("unexpected chunk size");
                        }
                    }
                    
                    //if there was no backing to load the chunk (e.g. chunk file missing) then create a new blank one
                    if (chunk == null)
                    {   
                        chunk = new Image(Bands, w, h);
                    }
                    
                    if (HasMask)
                    {
                        chunk.CreateMask(initialMaskValue);
                        if (hasSavedMask)
                        {
                            chunk.SaveMask();
                        }
                    }
                    
                    //remember chunk so that we take the fast path next time
                    if (chunks != null)
                    {
                        chunks[chunkRow, chunkCol] = chunk;
                    }
                    else
                    {
                        chunkCache[new Vector2(chunkRow, chunkCol)] = chunk;
                    }
                }
            }

            return chunk;
        }

        /// <summary>
        /// Get a chunk that's already in memory or in the chunkCache disk cache.
        /// </summary>
        private Image GetExistingChunk(int chunkRow, int chunkCol)
        {
            return chunks != null ? chunks[chunkRow, chunkCol] : chunkCache[new Vector2(chunkRow, chunkCol)];
        }

        public override void CreateMask(bool initialValue = false)
        {
            _hasMask = true;
            initialMaskValue = initialValue;
            for (int row = 0; row < chunkRows; row++)
            {
                for (int col = 0; col < chunkCols; col++)
                {
                    Image chunk = GetExistingChunk(row, col);
                    if (chunk != null)
                    {
                        chunk.CreateMask(initialValue);
                    }
                }
            }
        }
        
        public override void DeleteMask()
        {
            _hasMask = hasSavedMask = initialMaskValue = false;
            for (int row = 0; row < chunkRows; row++)
            {
                for (int col = 0; col < chunkCols; col++)
                {
                    Image chunk = GetExistingChunk(row, col);
                    if (chunk != null)
                    {
                        chunk.DeleteMask();
                    }
                }
            }
        }

        public override void SaveMask()
        {
            if (!HasMask || hasSavedMask)
            {
                throw new InvalidOperationException();
            }
            for (int row = 0; row < chunkRows; row++)
            {
                for (int col = 0; col < chunkCols; col++)
                {
                    Image chunk = GetExistingChunk(row, col);
                    if (chunk != null)
                    {
                        chunk.SaveMask();
                    }
                }
            }
        }

        public override void RestoreMask()
        {
            if (!hasSavedMask)
            {
                throw new InvalidOperationException();
            }
            hasSavedMask = false;
            for (int row = 0; row < chunkRows; row++)
            {
                for (int col = 0; col < chunkCols; col++)
                {
                    Image chunk = GetExistingChunk(row, col);
                    if (chunk != null)
                    {
                        chunk.RestoreMask();
                    }
                }
            }
        }

        public override bool IsValid(int row, int col)
        {
            if (!HasMask)
            {
                return true;
            }
            return GetChunk(row / chunkSize, col / chunkSize).IsValid(row % chunkSize, col% chunkSize);
        }

        public override bool IsValid(int i)
        {
            return IsValid(i / Width, i % Width);
        }

        public override void SetMaskValue(int row, int col, bool value)
        {
            GetChunk(row / chunkSize, col / chunkSize).SetMaskValue(row % chunkSize, col% chunkSize, value);
        }

        public override void SetMaskValue(int i, bool value)
        {
            SetMaskValue(i / Width, i % Width, value);
        }

        public override bool BandValuesEqual(int i, float[] perBandValues)
        {
            for (int b = 0; b < Bands; b++)
            {
                if (!this[b, i / Width, i % Width].Equals(perBandValues[b]))
                {
                    return false;
                }
            }
            return true;
        }

        public override void SetBandValues(int i, float[] perBandValues)
        {
            for (int b = 0; b < Bands; b++)
            {
                this[b, i / Width, i % Width] = perBandValues[b];
            }
        }

        public override float[] GetBandValues(int i)
        {
            float[] result = new float[Bands];
            for (int b = 0; b < Bands; b++)
            {
                result[b] = this[b, i / Width , i % Width];
            }
            return result;
        }

        public override void ApplyInPlace(int band, Func<float, float> f, bool applyToMaskedValues = false)
        {
            for (int r = 0; r < Height; r++)
            {
                for (int c = 0; c < Width; c++)
                {
                    if (applyToMaskedValues || IsValid(r, c))
                    {
                        this[band, r, c] = f(this[band, r, c]);
                    }
                }
            }
        }

        public override IEnumerator<float> GetEnumerator(bool includeInvalidValues)
        {
            for (int b = 0; b < Bands; b++)
            {
                for (int r = 0; r < Height; r++)
                {
                    for (int c = 0; c < Width; c++)
                    {
                        if (includeInvalidValues || IsValid(r, c))
                        {
                            yield return this[b, r, c];
                        }
                    }
                }
            }
        }

        public override float[] GetBandData(int band)
        {
            if (CanDensify())
            {
                return Densify().GetBandData(band);
            }
            else
            {
                throw new NotImplementedException("returning band for non-densifiable image is not implemented");
            }
        }

        public override int AddBand()
        {
            throw new NotImplementedException();
        }
    }

    public class SparseBinaryImage : BinaryImage
    {
        private bool[,][,] images;

        private int width;
        private int height;
        private int chunkSize;
        private int chunkRows;
        private int chunkCols;

        public SparseBinaryImage(int width, int height, int chunkSize = 256)
        {
            this.width = width;
            this.height = height;
            this.chunkSize = chunkSize;
            chunkRows = (int)Math.Ceiling((float)height / chunkSize);
            chunkCols = (int)Math.Ceiling((float)width / chunkSize);
            images = new bool[chunkRows, chunkCols][,];
        }

        public override bool this[int row, int col]
        {
            get
            {
                int chunkRow = row / chunkSize;
                int chunkCol = col / chunkSize;
                if (images[chunkRow, chunkCol] == null)
                {
                    return false;
                }
                else
                {
                    return images[chunkRow, chunkCol][row % chunkSize, col % chunkSize];
                }
            }

            set
            {
                int chunkRow = row / chunkSize;
                int chunkCol = col / chunkSize;
                if (images[chunkRow, chunkCol] == null)
                {
                    int chunkHeight = chunkRow < chunkRows - 1 ? chunkSize : height - chunkSize * (chunkRows - 1);
                    int chunkWidth = chunkCol < chunkCols - 1 ? chunkSize : width - chunkSize * (chunkCols - 1);
                    images[chunkRow, chunkCol] = new bool[chunkHeight, chunkWidth];
                }
                images[chunkRow, chunkCol][row % chunkSize, col % chunkSize] = value;
            }
        }
    }

    /// <summary>
    /// Sparse GIS image backed by an image file.
    /// Applies the standard read/write converters that normalize the band values to [0, 1].
    /// File format must support partial reads, currently only GDALSerializer does.
    /// The chunks are loaded lazily from disk.  Call Populate() to load them all.
    /// </summary>
    public class SparseGISImage : SparseImage
    {
        public const int CHUNK_SIZE = 512;
        public const int CHUNK_CACHE_SIZE = 400; //important: cache size > 0 limits memory usage

        private bool byteDataIssRGB;

        public SparseGISImage(string path, CameraModel cameraModel = null, bool byteDataIssRGB = true)
            : base(path, chunkSize: CHUNK_SIZE, cacheSize: CHUNK_CACHE_SIZE)
        {
            this.CameraModel = cameraModel;
            this.byteDataIssRGB = byteDataIssRGB;
        }

        public SparseGISImage(int bands, int width, int height, bool byteDataIssRGB = true)
            : base(bands, width, height, CHUNK_SIZE, CHUNK_CACHE_SIZE)
        {
            this.byteDataIssRGB = byteDataIssRGB;
        }

        public SparseGISImage(SparseGISImage that) : base(that)
        {
            this.byteDataIssRGB = that.byteDataIssRGB;
        }

        public override Image Instantiate(int bands, int width, int height)
        {
            return new SparseGISImage(bands, width, height);
        }

        public override object Clone()
        {
            return new SparseGISImage(this);
        }

        protected override IImageConverter GetReadConverter()
        {
            return byteDataIssRGB ?
                ImageConverters.ValueRangeSRGBToNormalizedImageLinearRGB : ImageConverters.ValueRangeToNormalizedImage;
        }

        protected override IImageConverter GetWriteConverter()
        {
            return byteDataIssRGB ?
                ImageConverters.NormalizedImageLinearRGBToValueRangeSRGB : ImageConverters.NormalizedImageToValueRange;
        }
    }
}
