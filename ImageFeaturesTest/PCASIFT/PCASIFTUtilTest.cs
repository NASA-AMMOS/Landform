using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.ImageFeatures;

namespace ImageFeaturesTest.PCASIFT
{
    [TestClass]
    public class PCASIFTUtilTest
    {
        // Partition space for NormalizeVector(float[] vector):
        //     - vector.Length: =1, >1
        //     - contains negative numbers: yes, no
        [TestMethod]
        public void NormalizeVectorTest()
        {
            float[] vec1 = new float[] { 2 };
            float[] res1 = new float[] { 1 };
            CollectionAssert.AreEquivalent(res1, PCAUtil.NormalizeVector(vec1));

            float[] vec3 = new float[] { 2, -1, 4.5f, 2, -5, 42 };
            float total = 9.416666666667f;
            float[] res2 = new float[] { 2 / total, -1 / total, 4.5f / total, 2 / total, -5 / total, 42 / total };
            CollectionAssert.AreEquivalent(res2, PCAUtil.NormalizeVector(vec3));
        }
    }
}
