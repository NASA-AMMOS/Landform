namespace JPLOPS.Imaging
{
    /// <summary>
    /// Image serializers are responsible for reading and saving Images
    /// and metadata
    /// </summary>
    public abstract class ImageSerializer
    {
        /// <summary>
        /// Reads an image from disk and uses the specified
        /// converter to map from the raw data type to the
        /// normalized form expected by the Image class.
        /// The type parameter for convert is determined by 
        /// inspection on the underlying file.
        /// If a fillValue is defined then the image mask will be true for any pixels who match the fill values
        /// 
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="converter"></param>
        /// <param name="fillValue">A list of per band values.  Length must equal the number of bands in the image</param>
        /// <param name="useFillValueFromFile">if the file contains information about invalid pixels use that to generate the mask</param>
        /// <returns></returns>
        public abstract Image Read(string filename, IImageConverter converter, float[] fillValue = null, bool useFillValueFromFile = false);

        /// <summary>
        /// Reads an image with the default read converter
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public Image Read(string filename)
        {
            return Read(filename, DefaultReadConverter());
        }

        /// <summary>
        /// Writes an image back to disk.  Uses the convter to
        /// map from image normalzied form to the expected data type T
        /// If a fill value is specified then any masked pixels in the image will use these values instead
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="filename"></param>
        /// <param name="image"></param>
        /// <param name="converter"></param>
        /// <param name="fillValue"></param>
        public abstract void Write<T>(string filename, Image image, IImageConverter converter, float[] fillValue = null);

        /// <summary>
        /// Writes  an image with the default write converter
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public void Write<T>(string filename, Image image)
        {
            Write<T>(filename, image, DefaultWriteConverter());
        }

        /// <summary>
        /// Returns a list of extensions handled by this image serializer, extensions should include the "." as in ".jpg"
        /// </summary>
        /// <returns></returns>
        public abstract string[] GetExtensions();

        public abstract IImageConverter DefaultReadConverter();

        public abstract IImageConverter DefaultWriteConverter();

        /// <summary>
        /// Register this serializer's extension with the ImageSerializers class
        /// </summary>
        public void Register(ImageSerializers map)
        {
            map.Register(GetExtensions(), this);
        }

        public void Register()
        {
            Register(ImageSerializers.Instance);
        } 
    }
}
