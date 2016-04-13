using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace OPS.Imaging
{
    /// <summary>
    /// Image serializers are responsible for reading and saving GenericImages
    /// and metadata
    /// </summary>
    public interface IImageSeralizer<T>
    {        
        GenericImage<T> Read(string filename);
        void Write(string filename, GenericImage<T> image);
    }
}
