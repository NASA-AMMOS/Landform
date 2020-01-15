using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class TextureProjector : JsonDataProduct
    {
        public int ImageWidth;
        public int ImageHeight;

        public Guid TextureGuid;

        public TextureProjector() { }

        public TextureProjector(Image image, Matrix meshToImage)
        {
            this.ImageWidth = image.Width;
            this.ImageHeight = image.Height;
            this.CameraModel = image.CameraModel;
            this.MeshToImage = meshToImage;
        }

        [JsonConverter(typeof(CameraModelConverter))]
        public CameraModel CameraModel;

        [JsonConverter(typeof(XNAMatrixConverter))]
        public Matrix MeshToImage;
    }
}
