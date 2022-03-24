using System;
using Newtonsoft.Json;
using JPLOPS.Util;
using JPLOPS.Geometry;
using JPLOPS.Imaging;

namespace JPLOPS.Pipeline
{
    public class MeshImagePairStats : NodeComponent
    {
        public int NumVerts;
        public int NumTris;

        public int ImageWidth;
        public int ImageHeight;

        public double MeshArea;
        public double UVArea;
        public double MinTriArea;
        public double MaxTriArea;

        public int NumPixels { get { return ImageWidth * ImageHeight; } }

        public bool HasIndex;

        public MeshImagePairStats() { }

        public MeshImagePairStats(MeshImagePair mip)
        {
            Update(mip);
        }

        public void Update(MeshImagePair mip)
        {
            Update(mip.Mesh, mip.Image, mip.Index);
        }

        public void Update(Mesh mesh, Image image, Image index)
        {
            if (mesh != null)
            {
                NumVerts = mesh.Vertices.Count;
                NumTris = mesh.Faces.Count;
                if (NumTris > 0)
                {
                    MinTriArea = double.PositiveInfinity;
                    MaxTriArea = double.NegativeInfinity;
                    foreach (var tri in mesh.Triangles())
                    {
                        double a = tri.Area();
                        MeshArea += a;
                        MinTriArea = Math.Min(MinTriArea, a);
                        MaxTriArea = Math.Max(MaxTriArea, a);
                    }
                }
                if (mesh.HasUVs)
                {
                    UVArea = mesh.ComputeUVArea();
                }
            }

            if (image != null)
            {
                ImageWidth = image.Width;
                ImageHeight = image.Height;
            }

            HasIndex = index != null;
        }

        public void Clear()
        {
            NumVerts = NumTris = 0;
            MeshArea = UVArea = MinTriArea = MaxTriArea = 0;
            ImageWidth = ImageHeight = 0;
            HasIndex = false;
        }

        private static string[] noSerialize = new string[] { "NodeComponent.Node", "MeshImagePairStats.NumPixels" };

        public string Serialize()
        {
            return JsonHelper.ToJson(this, autoTypes: false, ignoreProperties: noSerialize);
        }
        
        public static MeshImagePairStats Deserialize(string str)
        {
            return JsonHelper.FromJson<MeshImagePairStats>(str, autoTypes: false, ignoreProperties: noSerialize);
        }
    }

    public class MeshImagePairStatsConverter : JsonConverter
    {
        public override bool CanRead
        {
            get { return true; }
        }
        
        public override bool CanWrite
        {
            get { return true; }
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(MeshImagePairStats);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, ((MeshImagePairStats)value).Serialize());
        }
        
        public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
        {
            return MeshImagePairStats.Deserialize(serializer.Deserialize<string>(reader));
        }
    }
}
