using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OPS.Imaging.Serializers;

namespace OPS.Imaging
{
    /// <summary>
    /// This is the primary image class.  It stores data in a floating point format
    /// to enable generalized operations on a large variety of image types.
    /// </summary>
    public class Image : GenericImage<float>
    {

        public Image(int bands, int width, int height) : base(bands, width,height) { }


        public Image(string filename)
        {

        }

    }
}
