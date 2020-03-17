using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    public static class CacheParams
    {
        public const int CHUNK_SIZE = 512;
        public const int CACHE_SIZE = 400;
    }

    public class SparseDEMImage : SparseImage
    {
        public SparseDEMImage(string path) : base(path, chunkSize: CacheParams.CHUNK_SIZE, cacheSize: CacheParams.CACHE_SIZE, diskBackedCache: true)
        {
        }

        protected override IImageConverter GetReadConverter()
        {
            return ImageConverters.PassThrough;
        }

        protected override IImageConverter GetWriteConverter()
        {
            return ImageConverters.PassThrough;
        }
    }
}
