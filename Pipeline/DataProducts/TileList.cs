using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Microsoft.Xna.Framework;
using JPLOPS.MathExtensions;
using JPLOPS.Pipeline.TilingServer;

namespace JPLOPS.Pipeline
{
    public class TileList : JsonDataProduct
    {
        public string MeshExt;
        public string ImageExt;

        public bool HasIndexImages;

        public TilingScheme TilingScheme;

        public TextureMode TextureMode;

        public List<string> LeafNames;
        public List<string> ParentNames;

        [JsonConverter(typeof(XNAMatrixJsonConverter))]
        public Matrix RootTransform = Matrix.Identity;
    }
}
