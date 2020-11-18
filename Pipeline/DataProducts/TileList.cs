using System;
using System.Collections.Generic;
using System.Linq;
using OPS.Pipeline.TilingServer;

namespace OPS.Pipeline
{
    public class TileList : JsonDataProduct
    {
        public const string INDEX_FILE_SUFFIX = "_index";
        public const string INDEX_FILE_EXT = ".tif";
        public string MeshExt;
        public string ImageExt;
        public string MeshFrame;
        public bool HasIndexImages;
        public TilingScheme TilingScheme;
        public TextureMode TextureMode;
        public List<string> LeafNames;
        public List<string> ParentNames;
    }
}
