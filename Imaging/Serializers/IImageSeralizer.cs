using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace OPS.Imaging
{
    /// <summary>
    /// Image serializers are responsible for reading and saving Images
    /// and metadata
    /// </summary>
    public interface IImageSeralizer
    {        
        /// <summary>
        /// Reads an image from disk and uses the specified
        /// converter to map from the raw data type to the
        /// normalized form expected by the Image class.
        /// The type parameter for convert is determeined by 
        /// inspection on the underlying file
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        Image Read(string filename, IImageConverter converter);

        /// <summary>
        /// Writes an image back to disk.  Uses the convter to
        /// map from image normalzied form to the expected data type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="filename"></param>
        /// <param name="convertedImage"></param>
        void Write<T>(string filename, Image image, IImageConverter converter);
    }
}
