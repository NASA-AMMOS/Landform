using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
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
}
