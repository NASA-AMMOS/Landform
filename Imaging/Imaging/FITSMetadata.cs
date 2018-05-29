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
            var f = new nom.tam.fits.Fits(filename);
            var hdu = (ImageHDU)f.GetHDU(0);
            var cursor = hdu.Header.GetCursor();
            this.rawHeader.Add(NULL_GROUP, new Dictionary<string, string>());
            var nullGroup = this.rawHeader[NULL_GROUP];
            // Read header
            while (cursor.MoveNext())
            {
                var cur = (HeaderCard)((DictionaryEntry) cursor.Current).Value;
                if (cur.Key != "COMMENT" && cur.Key != "END")
                {
                    nullGroup.Add(cur.Key, cur.Value);
                }
            }

            if(ReadAsInt("NAXIS") != 2)
            {
                throw new ImageSerializationException("Unsupported  NAXIS in FITS file");
            }
            this.Bands = 1; // Right now we only support reading files with a single band
            this.Width = ReadAsInt("NAXIS1");
            this.Height = ReadAsInt("NAXIS2");

        }

    }
}
