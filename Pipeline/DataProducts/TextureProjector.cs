using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.MathExtensions;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class TextureProjector : JsonDataProduct
    {
        public int ImageWidth;
        public int ImageHeight;

        public Guid TextureGuid;

        [JsonConverter(typeof(CameraModelConverter))]
        public CameraModel CameraModel;

        [JsonConverter(typeof(XNAMatrixJsonConverter))]
        public Matrix MeshToImage;

        public TextureProjector() { }

        public TextureProjector(Image image, Matrix meshToImage)
        {
            this.ImageWidth = image.Width;
            this.ImageHeight = image.Height;
            this.CameraModel = image.CameraModel;
            this.MeshToImage = meshToImage;
        }
    }
}
