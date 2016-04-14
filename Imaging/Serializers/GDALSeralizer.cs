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
    public class GDALSeralizer : IImageSeralizer
    {
        public Image Read(string filename, IImageConverter converter)
        {
            throw new NotImplementedException();
        }

        public void Write<T>(string filename, Image image, IImageConverter converter)
        {
            throw new NotImplementedException();
        }
    }
}
