using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Geometry;

namespace OPS.Pipeline
{
    public class PlyGZDataProduct : DataProduct
    {
        public PlyGZDataProduct() { }

        public PlyGZDataProduct(Mesh mesh)
        {
            Mesh = mesh;
        }

        public Mesh Mesh;

        //TODO: add serialization APIs that read/write streams
        //https://github.jpl.nasa.gov/OnSight/Landform/issues/392
        //for now use temp files

        public override void Deserialize(byte[] data)
        {
            TemporaryFile.GetAndDelete(".ply", (fn) =>
            {
                File.WriteAllBytes(fn, Compression.Decompress(data));
                Mesh = Mesh.Load(fn);
            });
        }

        public override byte[] Serialize()
        {
            byte[] res = null;
            TemporaryFile.GetAndDelete(".ply", (fn) =>
            {
                Mesh.Save(fn);
                res = Compression.Compress(File.ReadAllBytes(fn));
            });
            return res;
        }
    }
}
