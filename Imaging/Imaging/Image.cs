using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OPS.Imaging;

namespace OPS.Imaging
{
    /// <summary>
    /// This is the primary image class.  It stores data in a floating point format
    /// to enable generalized operations on a large variety of image types.
    /// 
    /// Common image operations should be implemented here
    /// 
    /// When used to store RGB values are stored in normalized 0-1 form
    /// 
    /// </summary>
    public class Image : GenericImage<float>
    {

        /// <summary>
        /// Creates a new blank image with the specified resolution and bands
        /// </summary>
        /// <param name="bands"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        public Image(int bands, int width, int height) : base(bands, width, height) { }


        /// <summary>
        /// Loads a new image from a file
        /// </summary>
        /// <param name="filename"></param>
        /// <param name=""></param>
        public static Image Load<T>(string filename, IImageSeralizer<T> serializer, IImageConverter<T> converter)
        {
            throw new NotImplementedException();
        }


    }
}
