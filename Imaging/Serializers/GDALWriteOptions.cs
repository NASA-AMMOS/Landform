using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    public class GDALWriteOptions
    {

        public float? FillValue;
        public int JPEGCompressonLevel;

        public GDALWriteOptions(int jpgQuality = 75, float? fillValue = null)
        {
            JPEGCompressonLevel = jpgQuality;
            FillValue = fillValue;
        }

        public string[] OptionString
        {
            get
            {
                return new string[] { "QUALITY=" + JPEGCompressonLevel };
            }
        }
    }
}
