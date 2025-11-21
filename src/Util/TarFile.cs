using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace JPLOPS.Util
{
    public class TarFile
    {
        public static int Extract(string filename, string dir = null)
        {
            try
            {
                if (dir == null)
                {
                    dir = Path.GetDirectoryName(filename);
                }
                
                if (filename.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    using (var stream = new GZipStream(File.OpenRead(filename), CompressionMode.Decompress))
                    {
                        return Extract(stream, dir);
                    }
                }
                else
                {
                    using (var stream = File.OpenRead(filename))
                    {
                        return Extract(stream, dir);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new IOException("error extracting tar file " + filename + " to " + dir + ": " + ex.Message, ex);
            }
        }

        //https://stackoverflow.com/a/51975178
        public static int Extract(Stream stream, string dir)
        {
            var buf = new byte[2 * 1024 * 1024];

            int read(int sz, Stream str)
            {
                int nr = 0;
                while (nr < sz)
                {
                    int r = str.Read(buf, nr, sz);
                    if (r <= 0)
                    {
                        return nr;
                    }
                    nr += r;
                }
                return nr;
            }

            bool expect(int sz, Stream str)
            {
                return read(sz, str) == sz;
            }

            int numExtracted = 0;

            void error(string what)
            {
                throw new IOException("error extracting " + what + " for file " + numExtracted +
                                      " from tar at byte " + stream.Position);
            }

            while (true)
            {
                if (!expect(100, stream))
                {
                    error("name");
                }
                var name = Encoding.ASCII.GetString(buf, 0, 100).Trim('\0');

                if (String.IsNullOrWhiteSpace(name))
                {
                    break;
                }

                if (!expect(24, stream))
                {
                    error("header");
                }

                if (!expect(12, stream))
                {
                    error("size");
                }
                string szStr = Encoding.ASCII.GetString(buf, 0, 12).Trim('\0').Trim();
                ulong fsz = Convert.ToUInt64(szStr, 8); //octal

                if (!expect(376, stream))
                {
                    error("header");
                }

                //100 + 24 + 12 + 376 = 512

                var file = Path.Combine(dir, name);
                if (!Directory.Exists(Path.GetDirectoryName(file)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                }

                using (var fstr = File.Open(file, FileMode.OpenOrCreate, FileAccess.Write)) //overwrite
                {
                    int gotBytes = 0;
                    for (ulong fr = 0; fr < fsz; fr += (ulong)gotBytes)
                    {
                        ulong tryBytes = fsz - fr;
                        if (tryBytes > (ulong)(buf.Length))
                        {
                            tryBytes = (ulong)(buf.Length);
                        }
                        gotBytes = read((int)tryBytes, stream);
                        if ((ulong)gotBytes < tryBytes)
                        {
                            error("data");
                        }
                        fstr.Write(buf, 0, gotBytes);
                    }
                }

                numExtracted++;

                int pad = 512 - (int)(fsz % 512);
                if (pad > 0 && pad < 512)
                {
                    if (!expect(pad, stream))
                    {
                        error("padding");
                    }
                }
            }

            return numExtracted;
        }
    }
}
