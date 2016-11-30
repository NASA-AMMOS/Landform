using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Imaging;

namespace ImagingTest.Serializers
{
    [TestClass]
    public class PDSMetadataTest
    {
        [TestMethod]
        public void PDSMetadata()
        {

            string[] files = new string[] 
            {
                @"ML0_451292526RCX_S0311094MCAM02555M1.IMG",
                @"NLB_451025090ARMLF0311052NCAM00493M1.IMG",
                @"NLB_451557756RASLF0311330NCAM00353M1.IMG",
                @"NLB_451649560RNGLF0311330NCAM12813M1.IMG"
            };

            foreach(var f in files)
            {
                var m = new PDSMetadata(f);
            }
        }
    }
}
