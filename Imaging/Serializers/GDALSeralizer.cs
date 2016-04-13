using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using OSGeo.GDAL;
using System.IO;


namespace OPS.Imaging
{

    /// <summary>
    /// Reads all image types supported by GDAL
    /// </summary>
    public class GDALSeralizer<T> : IImageSeralizer<T>
    {
        public GenericImage<T> Read(string filename)
        {
            throw new NotImplementedException();
        }

        public void Write(string filename, GenericImage<T> image)
        {
            throw new NotImplementedException();
        }
    }
}
