using nom.tam.fits;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    public class FITSMetadata : RawMetadata
    {
        public FITSMetadata() : base()
        {

        }

        public FITSMetadata(string filename) : base()
        {
            var f = new nom.tam.fits.Fits(filename, System.IO.FileAccess.Read);
            var hdu = (ImageHDU)f.GetHDU(0);
            //var cursor = hdu.Header.GetCursor();
            this.rawHeader.Add(NULL_GROUP, new Dictionary<string, string>());
            var nullGroup = this.rawHeader[NULL_GROUP];
            // Read header
            foreach (var current in hdu.Header)
            {
                var cur = (HeaderCard)((DictionaryEntry)current).Value;
                if (cur.Key != "COMMENT" && cur.Key != "END")
                {
                    nullGroup.Add(cur.Key, cur.Value);
                }
            }

            if (ReadAsInt("NAXIS") != 2)
            {
                throw new ImageSerializationException("Unsupported  NAXIS in FITS file");
            }
            this.Bands = 1; // Right now we only support reading files with a single band
            this.Height = ReadAsInt("NAXIS1");
            this.Width = ReadAsInt("NAXIS2");
        }

    }
}
