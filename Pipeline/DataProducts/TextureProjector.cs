using System;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using JPLOPS.MathExtensions;
using JPLOPS.Imaging;

namespace JPLOPS.Pipeline
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
