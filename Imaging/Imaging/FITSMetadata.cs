using nom.tam.fits;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace JPLOPS.Imaging
{
    public class FITSMetadata : RawMetadata
    {
        public FITSMetadata() : base()
        {

        }

        public FITSMetadata(string filename) : base()
        {
            var f = new nom.tam.fits.Fits(filename, System.IO.FileAccess.Read);
            BasicHDU hdu = f.ReadHDU();

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

            // the extend keyword just indicates an extension may be present
            //  but doesn't guarantee there will be one
            bool extensionPresent = nullGroup.ContainsKey("EXTEND") && nullGroup["EXTEND"] == "T";
            if (extensionPresent)
            {
                try
                {
                    hdu = f.ReadHDU();
                    extensionPresent = hdu != null;
                }
                catch (EndOfStreamException)
                {
                    extensionPresent = false;
                }
            }

            // handle image extension
            if (extensionPresent)
            {
                this.rawHeader.Add("IMAGE", new Dictionary<string, string>());

                var imageGroup = this.rawHeader["IMAGE"];

                int subImageIdx = -1;
                bool inSubImage = false;
                foreach (var current in hdu.Header)
                {
                    var cur = (HeaderCard)((DictionaryEntry)current).Value;

                    if((cur.Key == "XTENSION"))
                    {
                        if (cur.Value == "TABLE")
                            continue;
                        else if(cur.Value != "IMAGE")
                            throw new NotImplementedException("only image extensions handled currently");
                    }
                    
                    if ((subImageIdx >= 0) && (subImageIdx < ReadAsInt("IMAGE", "NSUBIMG")))
                    {
                        if (cur.Key.StartsWith("COMMENT"))
                        {
                            if (!inSubImage)
                            {
                                inSubImage = true;
                            }
                            else
                            { 
                                subImageIdx++;
                                string subImageName = "SUBIMAGE_" + subImageIdx;
                                inSubImage = subImageIdx < ReadAsInt("IMAGE", "NSUBIMG");
                                if (inSubImage && !this.rawHeader.ContainsKey(subImageName))
                                {
                                    this.rawHeader.Add(subImageName, new Dictionary<string, string>());
                                }
                            }
                        }
                        else
                        {
                            string subImageName = "SUBIMAGE_" + subImageIdx;
                            this.rawHeader[subImageName].Add(cur.Key, cur.Value);
                        }
                    }
                    else if (!cur.Key.StartsWith("COMMENT") && !cur.Key.StartsWith("HISTORY") && !cur.Key.StartsWith("END"))
                    {
                        imageGroup.Add(cur.Key, cur.Value);
                        if (cur.Key == "NSUBIMG")
                        {
                            subImageIdx++;
                            string subImageName = "SUBIMAGE_" + subImageIdx;
                            this.rawHeader.Add(subImageName, new Dictionary<string, string>());
                        }
                    }
                }
            }

            //get dimensions of image
            int naxis = ReadAsInt("NAXIS");
            if (naxis != 0)
            {
                this.Width = ReadAsInt("NAXIS1");
                this.Height = ReadAsInt("NAXIS2");
            }
            else
            {
                naxis = ReadAsInt("IMAGE", "NAXIS");
                this.Width = ReadAsInt("IMAGE", "NAXIS1");
                this.Height = ReadAsInt("IMAGE", "NAXIS2");
            }

            if (naxis != 2)
            {
                throw new ImageSerializationException("Unsupported  NAXIS in FITS file");
            }
            this.Bands = 1; // Right now we only support reading files with a single band
        }
    }
}
