using System;
using Xunit;
using JPLOPS.Util;
using System.IO;

namespace UtilTest
{
    public class TemporaryFileTest
    {
        [Fact]
        public void TempFileGetAndMoveTest()
        {
            string tmpName = "";
            if(File.Exists("getAndMove.txt"))
            {
                File.Delete("getAndMove.txt");
            }
            TemporaryFile.GetAndMove("getAndMove.txt", tmp =>
            {
                tmpName = tmp;
                Assert.True(Directory.Exists(Path.GetDirectoryName(tmp)));
                File.WriteAllText(tmp, "Hello world");
                Assert.False(File.Exists("getAndMove.txt"));
            });
            Assert.True(File.Exists("getAndMove.txt"));
            Assert.Equal("Hello world", File.ReadAllText("getAndMove.txt"));
            Assert.False(File.Exists(tmpName));
        }

        [Fact]
        public void TempFileGetAndDeleteTest()
        {
            string tmpName = "";
            TemporaryFile.GetAndDelete("getAndDel.txt", tmp =>
            {
                tmpName = tmp;
                Assert.True(Directory.Exists(Path.GetDirectoryName(tmp)));
                File.WriteAllText(tmp, "Goodbye world");
                Assert.False(File.Exists("getAndDel.txt"));
            });
            Assert.False(File.Exists("getAndDel.txt"));
            Assert.False(File.Exists(tmpName));
        }

        [Fact]
        public void TempFileGetAndDeleteMultipleTest()
        {
            String[] filelist = {};
            TemporaryFile.GetAndDeleteMultiple(5, ".foo", tmp =>
            {
                
                Assert.Equal(5, tmp.Length);
                filelist = tmp;
                foreach(var f in tmp)
                {
                    Assert.Equal(".foo", Path.GetExtension(f));
                    Assert.True(Directory.Exists(Path.GetDirectoryName(f)));
                    File.WriteAllText(f, "Goodbye world");
                    Assert.False(File.Exists("getAndDel.txt"));
                }
                
            });
            foreach (var f in filelist)
            {
                Assert.False(File.Exists(f));
  
            }
        }

        [Fact]
        public void TempFileOverrideDir()
        {
            string tmpDir = TemporaryFile.TemporaryDirectory;
            TemporaryFile.TemporaryDirectory = "different";
            TemporaryFile.GetAndDelete("tmpDirTest.txt", tmp =>
            {
                Assert.True(Directory.Exists("different"));
                Assert.Contains("different", tmp);
            });
            TemporaryFile.TemporaryDirectory = tmpDir;
        }
    }
}
