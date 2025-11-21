using Xunit;
using JPLOPS.Util;

namespace UtilTest
{
    public class PathHelperTest
    {
        [Fact]
        public void TestChangeDirectory()
        {
            Assert.Equal(@"bye/hello.txt", PathHelper.ChangeDirectory("test/hello.txt", "bye").Replace(@"\","/"));
            Assert.Equal(@"bye/hello.txt", PathHelper.ChangeDirectory(@"test/hello.txt", "bye").Replace(@"\","/"));
            Assert.Equal(@"/bye/hello.txt", PathHelper.ChangeDirectory(@"test/hello.txt", "/bye").Replace(@"\","/"));
            Assert.Equal(@"bye/hello.jpg",
                         PathHelper.ChangeDirectory(@"test/hello.txt", "bye/", "jpg").Replace(@"\","/"));
            Assert.Equal(@"bye/hello.png",
                         PathHelper.ChangeDirectory(@"test/hello.txt", "bye/", ".png").Replace(@"\","/"));
            Assert.Equal(@"bye/hello.png",
                         PathHelper.ChangeDirectory(@"hello.txt", "bye", ".png").Replace(@"\","/"));
            Assert.Equal(@"C:/doctor/who/hello.png",
                         PathHelper.ChangeDirectory(@"hello.txt", @"C:/doctor/who/", ".png").Replace(@"\","/"));
            Assert.Equal(@"C:/yes/way/hello.png",
                         PathHelper.ChangeDirectory(@"noway/hello.txt", @"C:/yes/way/", ".png").Replace(@"\","/"));
            Assert.Equal(@"C:/test", PathHelper.ChangeDirectory(@"noway/", @"C:/test").Replace(@"\","/"));
            Assert.Equal(@"C:/test/noway", PathHelper.ChangeDirectory(@"c:/noway", @"C:/test").Replace(@"\","/"));
        }
    }
}
