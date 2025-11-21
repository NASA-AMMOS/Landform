using System;
using Xunit;

namespace JPLOPS.TestExtensions
{
    public class AssertExtensionsTest
    {
        [Fact]
        public void AreSimilarTest()
        {
            AssertE.AreSimilar(1, 1);
        }
    }
}
