using System;
using System.Linq;
using System.Collections.Generic;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Newtonsoft.Json;
using OPS.Util;
using OPS.Geometry;
using OPS.Imaging;

namespace OPS.Pipeline
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

        public MeshImagePairStats() { }

        public MeshImagePairStats(MeshImagePair mip)
        {
            if (mip != null)
            {
                if (mip.Mesh != null)
                {
                    NumVerts = mip.Mesh.Vertices.Count;
                    NumTris = mip.Mesh.Faces.Count;
                    if (NumTris > 0)
                    {
                        MinTriArea = double.PositiveInfinity;
                        MaxTriArea = double.NegativeInfinity;
                        foreach (var tri in mip.Mesh.Triangles())
                        {
                            double a = tri.Area();
                            MeshArea += a;
                            MinTriArea = Math.Min(MinTriArea, a);
                            MaxTriArea = Math.Max(MaxTriArea, a);
                        }
                    }
                    if (mip.Mesh.HasUVs)
                    {
                        UVArea = mip.Mesh.ComputeUVArea();
                    }
                }
                if (mip.Image != null)
                {
                    ImageWidth = mip.Image.Width;
                    ImageHeight = mip.Image.Height;
                }
            }
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

    public class MeshImagePairStatsConverter : JsonConverter, IPropertyConverter
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

        public object FromEntry(DynamoDBEntry entry)
        {
            return MeshImagePairStats.Deserialize(entry.AsString());
        }

        public DynamoDBEntry ToEntry(object value)
        {
            return ((MeshImagePairStats)value).Serialize();
        }
    }
}
