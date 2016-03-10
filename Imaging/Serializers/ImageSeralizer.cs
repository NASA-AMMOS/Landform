using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging.Imaging;

namespace OPS.Imaging.Serializers
{
    /// <summary>
    /// Image serializers are responsible for reading and saving GenericImages
    /// and metadata
    /// </summary>
    interface ImageSeralizer<T>
    {        
        Image Read(string filename, ImageConverter<T> converter);
        void Write(string filename, ImageConverter<T> converter);
    }
}
