using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Util;

namespace UtilTest
{
    [TestClass]
    public class PathHelperTest
    {
        [TestMethod]
        public void TestChangeDirectory()
        {
            Assert.AreEqual(@"bye\hello.txt", PathHelper.ChangeDirectory("test/hello.txt", "bye"));
            Assert.AreEqual(@"bye\hello.txt", PathHelper.ChangeDirectory(@"test\hello.txt", "bye"));
            Assert.AreEqual(@"\bye\hello.txt", PathHelper.ChangeDirectory(@"test\hello.txt", "\\bye"));
            Assert.AreEqual(@"bye/hello.jpg", PathHelper.ChangeDirectory(@"test\hello.txt", "bye/", "jpg"));
            Assert.AreEqual(@"bye\hello.png", PathHelper.ChangeDirectory(@"test\hello.txt", "bye\\", ".png"));
            Assert.AreEqual(@"bye\hello.png", PathHelper.ChangeDirectory(@"hello.txt", "bye", ".png"));
            Assert.AreEqual(@"C:\doctor\who\hello.png", PathHelper.ChangeDirectory(@"hello.txt", @"C:\doctor\who\", ".png"));
            Assert.AreEqual(@"C:\yes\way\hello.png", PathHelper.ChangeDirectory(@"noway\hello.txt", @"C:\yes\way\", ".png"));
            Assert.AreEqual(@"C:\test", PathHelper.ChangeDirectory(@"noway\", @"C:\test"));
            Assert.AreEqual(@"C:\test\noway", PathHelper.ChangeDirectory(@"c:\noway", @"C:\test"));
        }
    }
}
