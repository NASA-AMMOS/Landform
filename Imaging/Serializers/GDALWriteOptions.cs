namespace JPLOPS.Imaging
{
    public class GDALWriteOptions
    {

        public GDALWriteOptions()
        {
        }

        public virtual string[] OptionString
        {
            get
            {
                return null;
            }
        }
    }


    public class GDALJPGWriteOptions : GDALWriteOptions
    {
        public int JPEGCompressonLevel;

        public GDALJPGWriteOptions(int jpgQuality = 75)
        {
            JPEGCompressonLevel = jpgQuality;
        }

        public override string[] OptionString
        {
            get
            {
                return new string[] { "QUALITY=" + JPEGCompressonLevel };
            }
        }
    }

    public class GDALTIFFWriteOptions : GDALWriteOptions
    {
        //https://www.gdal.org/frmt_gtiff.html
        public enum CompressionType { JPEG, LZW, PACKBITS, DEFLATE, CCITTRLE, CCITTFAX3, CCITTFAX4, LZMA, ZSTD, LERC, LERC_DEFLATE, LERC_ZSTD, WEBP, NONE };

        public CompressionType Compression;

        public GDALTIFFWriteOptions(CompressionType compression = CompressionType.NONE)
        {
            this.Compression = compression;
        }

        public override string[] OptionString
        {
            get
            {
                return new string[] { "COMPRESS=" + Compression };
            }
        }
    }
}
