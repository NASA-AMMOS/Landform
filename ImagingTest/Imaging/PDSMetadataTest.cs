using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Imaging;
using Microsoft.Xna.Framework;
using System.IO;

namespace ImagingTest.Serializers
{
    [TestClass]
    [DeploymentItem("TestData", "TestData")]
    public class PDSMetadataTest
    {
        [TestMethod]
        public void PDSMetadataFileReadTest()
        {
            {
                string filename = Path.Combine("TestData", "img", "ML0_451292526RCX_S0311094MCAM02555M1.IMG");
                var m = new PDSMetadata(filename);
                Assert.AreEqual(26752, m.DataOffset);
                Assert.AreEqual(m.Width, 1408);
                Assert.AreEqual(m.Height, 1200);
                Assert.AreEqual(m.Bands, 3);
                Assert.AreEqual(1408, m.RecordBytes);
                Assert.AreEqual(8, m.BitDepth);
                Assert.AreEqual(typeof(byte), m.SampleType);
                Assert.AreEqual((uint)255, m.BitMask);

                Assert.AreEqual(typeof(CAHVOR), m.CameraModel.GetType());
                var cm = (CAHVOR)m.CameraModel;
                Vector3 mc_1 = new Vector3(8.414585e-01, 6.835634e-01, -1.974642e+00);
                Vector3 mc_2 = new Vector3(-5.092083e-01, 2.731827e-01, 8.161263e-01);
                Vector3 mc_3 = new Vector3(-2.645033e+03, -3.850412e+03, 4.976792e+02);
                Vector3 mc_4 = new Vector3(2.972422e+03, -1.740013e+03, 3.173833e+03);
                Vector3 mc_5 = new Vector3(-5.115142e-01, 2.723840e-01, 8.149505e-01);
                Vector3 mc_6 = new Vector3(-1.510000e-04, -1.391890e-01, -1.250336e+00);
                Assert.IsTrue(Vector3.AlmostEqual(mc_1, cm.C));
                Assert.IsTrue(Vector3.AlmostEqual(mc_2, cm.A));
                Assert.IsTrue(Vector3.AlmostEqual(mc_3, cm.H));
                Assert.IsTrue(Vector3.AlmostEqual(mc_4, cm.V));
                Assert.IsTrue(Vector3.AlmostEqual(mc_5, cm.O));
                Assert.IsTrue(Vector3.AlmostEqual(mc_6, cm.R));

            }

            {
                string filename = Path.Combine("TestData", "img", "NLB_451025090ARMLF0311052NCAM00493M1.IMG");
                var m = new PDSMetadata(filename);
                Assert.AreEqual(m.Width, 1024);
                Assert.AreEqual(m.Height, 1024);
                Assert.AreEqual(m.Bands, 5);
                Assert.AreEqual(typeof(ushort), m.SampleType);

                Assert.AreEqual(typeof(CAHV), m.CameraModel.GetType());
                var cm = (CAHV)m.CameraModel;
                Vector3 mc_1 = new Vector3(1.04791, 0.571407, -1.93105);
                Vector3 mc_2 = new Vector3(0.433947, 0.897764, 0.0754502);
                Vector3 mc_3 = new Vector3(-887.963, 990.629, 36.5519);
                Vector3 mc_4 = new Vector3(179.469, 375.167, 1262.74);
                Assert.IsTrue(Vector3.AlmostEqual(mc_1, cm.C));
                Assert.IsTrue(Vector3.AlmostEqual(mc_2, cm.A));
                Assert.IsTrue(Vector3.AlmostEqual(mc_3, cm.H));
                Assert.IsTrue(Vector3.AlmostEqual(mc_4, cm.V));
            }

            {
                string filename = Path.Combine("TestData", "img", "NLB_451557756RASLF0311330NCAM00353M1.IMG");
                var m = new PDSMetadata(filename);
                Assert.AreEqual(m.Width, 1024);
                Assert.AreEqual(m.Height, 1024);
                Assert.AreEqual(m.Bands, 1);
                Assert.AreEqual(typeof(ushort), m.SampleType);

                Assert.AreEqual(typeof(CAHV), m.CameraModel.GetType());
                var cm = (CAHV)m.CameraModel;
                Vector3 mc_1 = new Vector3(0.809503, 0.324769, -1.8358);
                Vector3 mc_2 = new Vector3(0.558137, -0.23828, 0.794788);
                Vector3 mc_3 = new Vector3(764.598, 1011.53, 403.962);
                Vector3 mc_4 = new Vector3(-613.528, 259.841, 1150.39);
                Assert.IsTrue(Vector3.AlmostEqual(mc_1, cm.C));
                Assert.IsTrue(Vector3.AlmostEqual(mc_2, cm.A));
                Assert.IsTrue(Vector3.AlmostEqual(mc_3, cm.H));
                Assert.IsTrue(Vector3.AlmostEqual(mc_4, cm.V));
            }

            {
                string filename = Path.Combine("TestData", "img", "NLB_451649560RNGLF0311330NCAM12813M1.IMG");
                var m = new PDSMetadata(filename);
                Assert.AreEqual(m.Width, 1024);
                Assert.AreEqual(m.Height, 1024);
                Assert.AreEqual(m.Bands, 1);
                Assert.AreEqual(32, m.BitDepth);
                Assert.AreEqual((uint)32767, m.BitMask);
                Assert.AreEqual(typeof(float), m.SampleType);

                Assert.AreEqual(typeof(CAHV), m.CameraModel.GetType());
                var cm = (CAHV)m.CameraModel;
                Vector3 mc_1 = new Vector3(0.733394, 0.781776, -1.98051);
                Vector3 mc_2 = new Vector3(-0.921673, 0.0942946, -0.376311);
                Vector3 mc_3 = new Vector3(-591.815, -1176.07, -191.985);
                Vector3 mc_4 = new Vector3(-929.662, 93.7434, 945.276);
                Assert.IsTrue(Vector3.AlmostEqual(mc_1, cm.C));
                Assert.IsTrue(Vector3.AlmostEqual(mc_2, cm.A));
                Assert.IsTrue(Vector3.AlmostEqual(mc_3, cm.H));
                Assert.IsTrue(Vector3.AlmostEqual(mc_4, cm.V));

            }

            {
                string filename = Path.Combine("TestData", "img", "FLB_509619692RAS_T0530000FHAZ00323M_.IMG");
                var m = new PDSMetadata(filename);
                Assert.AreEqual(m.Width, 64);
                Assert.AreEqual(m.Height, 64);
                Assert.AreEqual(m.Bands, 1);

                Assert.AreEqual(typeof(CAHVORE), m.CameraModel.GetType());
                var cm = (CAHVORE)m.CameraModel;
                Vector3 mc_1 = new Vector3(1.03304, -0.17145, -0.707908);
                Vector3 mc_2 = new Vector3(0.706662, 0.00032, 0.707551);
                Vector3 mc_3 = new Vector3(21.9636, 28.3687, 21.6746);
                Vector3 mc_4 = new Vector3(2.37507, 0.231257, 42.5149);
                Vector3 mc_5 = new Vector3(0.704793, -0.000541, 0.709413);
                Vector3 mc_6 = new Vector3(8.0e-06, -0.013391, -0.007308);
                Vector3 mc_7 = new Vector3(0.001868, 0.001572, 0.001665);

                Assert.IsTrue(Vector3.AlmostEqual(mc_1, cm.C));
                Assert.IsTrue(Vector3.AlmostEqual(mc_2, cm.A));
                Assert.IsTrue(Vector3.AlmostEqual(mc_3, cm.H));
                Assert.IsTrue(Vector3.AlmostEqual(mc_4, cm.V));
                Assert.IsTrue(Vector3.AlmostEqual(mc_5, cm.O));
                Assert.IsTrue(Vector3.AlmostEqual(mc_6, cm.R));
                Assert.IsTrue(Vector3.AlmostEqual(mc_7, cm.E));
                Assert.AreNotEqual(CAHVORE.PERSPECTIVE_LINEARITY, cm.Linearity);
                Assert.AreNotEqual(CAHVORE.FISHEYE_LINEARITY, cm.Linearity);
                Assert.AreEqual(0.37, cm.Linearity);
            }
        }

        [TestMethod]
        public void PDSMetadataCopyConstructerTest()
        {
            string filename = Path.Combine("TestData", "img", "ML0_451292526RCX_S0311094MCAM02555M1.IMG");
            var orig = new PDSMetadata(filename);
            var m = new PDSMetadata(orig);
            Assert.AreEqual(m.Width, 1408);
            Assert.AreEqual(m.Height, 1200);
            Assert.AreEqual(m.Bands, 3);
            Assert.AreEqual(1408, m.RecordBytes);
            Assert.AreEqual(8, m.BitDepth);
            Assert.AreEqual(typeof(byte), m.SampleType);
            Assert.AreEqual((uint)255, m.BitMask);

            Assert.AreEqual(typeof(CAHVOR), m.CameraModel.GetType());
            var cm = (CAHVOR)m.CameraModel;
            Vector3 mc_1 = new Vector3(8.414585e-01, 6.835634e-01, -1.974642e+00);
            Vector3 mc_2 = new Vector3(-5.092083e-01, 2.731827e-01, 8.161263e-01);
            Vector3 mc_3 = new Vector3(-2.645033e+03, -3.850412e+03, 4.976792e+02);
            Vector3 mc_4 = new Vector3(2.972422e+03, -1.740013e+03, 3.173833e+03);
            Vector3 mc_5 = new Vector3(-5.115142e-01, 2.723840e-01, 8.149505e-01);
            Vector3 mc_6 = new Vector3(-1.510000e-04, -1.391890e-01, -1.250336e+00);
            Assert.IsTrue(Vector3.AlmostEqual(mc_1, cm.C));
            Assert.IsTrue(Vector3.AlmostEqual(mc_2, cm.A));
            Assert.IsTrue(Vector3.AlmostEqual(mc_3, cm.H));
            Assert.IsTrue(Vector3.AlmostEqual(mc_4, cm.V));
            Assert.IsTrue(Vector3.AlmostEqual(mc_5, cm.O));
            Assert.IsTrue(Vector3.AlmostEqual(mc_6, cm.R));

            // Change all the things in the copy
            m.Width = 2;
            m.Height = 3;
            m.Bands = 4;
            m.RecordBytes = 5;
            m.BitDepth = 6;
            cm.C = new Vector3(1, 2, 3);
            cm.A = new Vector3(1, 2, 3);
            cm.H = new Vector3(1, 2, 3);
            cm.V = new Vector3(1, 2, 3);
            cm.O = new Vector3(1, 2, 3);
            cm.R = new Vector3(1, 2, 3);

            // Confirm nothing changed in the original
            Assert.AreEqual(orig.Width, 1408);
            Assert.AreEqual(orig.Height, 1200);
            Assert.AreEqual(orig.Bands, 3);
            Assert.AreEqual(1408, orig.RecordBytes);
            Assert.AreEqual(8, orig.BitDepth);
            Assert.AreEqual(typeof(byte), orig.SampleType);
            Assert.AreEqual((uint)255, orig.BitMask);

            Assert.AreEqual(typeof(CAHVOR), orig.CameraModel.GetType());
            var cm2 = (CAHVOR)orig.CameraModel;
            Assert.IsTrue(Vector3.AlmostEqual(mc_1, cm2.C));
            Assert.IsTrue(Vector3.AlmostEqual(mc_2, cm2.A));
            Assert.IsTrue(Vector3.AlmostEqual(mc_3, cm2.H));
            Assert.IsTrue(Vector3.AlmostEqual(mc_4, cm2.V));
            Assert.IsTrue(Vector3.AlmostEqual(mc_5, cm2.O));
            Assert.IsTrue(Vector3.AlmostEqual(mc_6, cm2.R));
        }


        [TestMethod]
        public void PDSMetadataAccessorTest()
        {
            string filename = Path.Combine("TestData", "img", "ML0_451292526RCX_S0311094MCAM02555M1.IMG");
            var m = new PDSMetadata(filename);

            Assert.IsTrue(m.HasGroup("GEOMETRIC_CAMERA_MODEL_PARMS"));
            Assert.IsFalse(m.HasGroup("Bob"));
            Assert.IsTrue(m.HasKey("MSL:COMMUNICATION_SESSION_ID"));
            Assert.IsTrue(m.HasKey("PDS_HISTORY_PARMS", "SOFTWARE_NAME"));
            Assert.AreEqual(21, m.Groups().Count);
            Assert.AreEqual(3, m.Keys("PDS_HISTORY_PARMS").Count);
            Assert.AreEqual("2014-04-22T15:32:53.619", m["PRODUCT_CREATION_TIME"]);
            Assert.AreEqual("0606ML0025550030301440E01_DXXX", m.ReadAsString("MSL:INPUT_PRODUCT_ID"));
            Assert.AreEqual("\"CODMAC LEVEL 1 to LEVEL 4 CONVERSION VIA MSSS MMMRDRGEN\"", m["PDS_HISTORY_PARMS", "PROCESSING_HISTORY_TEXT"]);
            Assert.AreEqual("CODMAC LEVEL 1 to LEVEL 4 CONVERSION VIA MSSS MMMRDRGEN", m.ReadAsString("PDS_HISTORY_PARMS", "PROCESSING_HISTORY_TEXT"));
            string[] sarr = m.ReadAsStringArray("GEOMETRIC_CAMERA_MODEL_PARMS", "MODEL_COMPONENT_ID");
            Assert.AreEqual("C", sarr[0]);
            Assert.AreEqual("A", sarr[1]);
            Assert.AreEqual("H", sarr[2]);
            Assert.AreEqual("V", sarr[3]);
            Assert.AreEqual("O", sarr[4]);
            Assert.AreEqual("R", sarr[5]);
            Assert.AreEqual(14.7209, m.ReadAsDouble("INSTRUMENT_STATE_PARMS", "VERTICAL_FOV"));
            Assert.AreEqual(0, m.ReadAsDouble("IMAGE_REQUEST_PARMS", "FILTER_NUMBER"));
            Assert.AreEqual(65, m.ReadAsDouble("IMAGE_REQUEST_PARMS", "INST_CMPRS_QUALITY"));
            Assert.AreEqual(1.2, m.ReadAsDouble("DERIVED_IMAGE_PARMS", "MSL:MINIMUM_FOCUS_DISTANCE"));
            double[] darr = m.ReadAsDoubleArray("SITE_COORDINATE_SYSTEM_PARMS", "ORIGIN_OFFSET_VECTOR");
            Assert.AreEqual(-158.193497, darr[0]);
            Assert.AreEqual(112.316025, darr[1]);
            Assert.AreEqual(-3.327696, darr[2]);
            Assert.AreEqual(19, m.ReadAsInt("LABEL_RECORDS"));
            Assert.AreEqual(1, m.ReadAsInt("RELEASE_ID"));
            Assert.AreEqual(2002555003, m.ReadAsLong("MSL:REQUEST_ID"));
            int[] iarr = m.ReadAsIntArray("ROVER_COORDINATE_SYSTEM_PARMS", "COORDINATE_SYSTEM_INDEX");
            Assert.AreEqual(31, iarr[0]);
            Assert.AreEqual(1094, iarr[1]);
            Assert.AreEqual(52, iarr[2]);
            Assert.AreEqual(106, iarr[3]);
            Assert.AreEqual(0, iarr[4]);
            Assert.AreEqual(0, iarr[5]);
            Assert.AreEqual(468, iarr[6]);
            Assert.AreEqual(268, iarr[7]);
            Assert.AreEqual(0, iarr[8]);
            Assert.AreEqual(0, iarr[9]);
            var dt = m.ReadAsDateTime("IMAGE_TIME");
            Assert.AreEqual(2014, dt.Year);
            Assert.AreEqual(20, dt.Day);
            Assert.AreEqual(4, dt.Month);
            Assert.AreEqual(19, dt.Hour);
            Assert.AreEqual(12, dt.Minute);
            Assert.AreEqual(37, dt.Second);
            Assert.AreEqual(798, dt.Millisecond);
        }
    }
}
