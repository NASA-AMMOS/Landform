using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Util;
using System.IO;

namespace UtilTest
{
    [TestClass]
    public class TemporaryFileTest
    {
        [TestMethod]
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
                Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(tmp)));
                File.WriteAllText(tmp, "Hello world");
                Assert.IsFalse(File.Exists("getAndMove.txt"));
            });
            Assert.IsTrue(File.Exists("getAndMove.txt"));
            Assert.AreEqual("Hello world", File.ReadAllText("getAndMove.txt"));
            Assert.IsFalse(File.Exists(tmpName));
        }

        [TestMethod]
        public void TempFileGetAndDeleteTest()
        {
            string tmpName = "";
            TemporaryFile.GetAndDelete("getAndDel.txt", tmp =>
            {
                tmpName = tmp;
                Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(tmp)));
                File.WriteAllText(tmp, "Goodbye world");
                Assert.IsFalse(File.Exists("getAndDel.txt"));
            });
            Assert.IsFalse(File.Exists("getAndDel.txt"));
            Assert.IsFalse(File.Exists(tmpName));
        }

        [TestMethod]
        public void TempFileGetAndDeleteMultipleTest()
        {
            String[] filelist = null;
            TemporaryFile.GetAndDeleteMultiple(5, ".foo", tmp =>
            {
                
                Assert.AreEqual(5, tmp.Length);
                filelist = tmp;
                foreach(var f in tmp)
                {
                    Assert.AreEqual(".foo", Path.GetExtension(f));
                    Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(f)));
                    File.WriteAllText(f, "Goodbye world");
                    Assert.IsFalse(File.Exists("getAndDel.txt"));
                }
                
            });
            foreach (var f in filelist)
            {
                Assert.IsFalse(File.Exists(f));
  
            }
        }

        [TestMethod]
        public void TempFileOverrideDir()
        {
            string tmpDir = TemporaryFile.TemporaryDirectory;
            TemporaryFile.TemporaryDirectory = "different";
            TemporaryFile.GetAndDelete("tmpDirTest.txt", tmp =>
            {
                Assert.IsTrue(Directory.Exists("different"));
                Assert.IsTrue(tmp.Contains("different"));
            });
            TemporaryFile.TemporaryDirectory = tmpDir;
        }
    }
}
