using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Pipeline.TilingServer;

namespace OPS.Pipeline
{
    public class LeafList : JsonDataProduct
    {
        public string MeshExt;
        public string ImageExt;
        public string MeshFrame;
        public TilingScheme TilingScheme;
        public List<string> LeafNames;
    }
}
