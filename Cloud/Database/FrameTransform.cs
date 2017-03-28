using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using System.ComponentModel.DataAnnotations.Schema;

namespace OPS.Cloud
{
    public static class TransformSource
    {
        public const string Prior = "prior";
        public const string Derived = "derived";
    }

    /// <summary>
    /// Represents the rotation and translation between two frames
    /// </summary>
    public class FrameTransform
    {
        public int Id { get; set; }
        public int FromFrameId { get; set; }
        public int ToFrameId { get; set; }
        public string TransformSource { get; set; }
        public double Error { get; set; }

        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double QX { get; set; }
        public double QY { get; set; }
        public double QZ { get; set; }
        public double QW { get; set; }

        public FrameTransform()
        {
        }

        public FrameTransform(int fromFrameId, int toFrameId, Vector3 translation, Quaternion rotation, string transformSource, double error)
        {
            this.FromFrameId = fromFrameId;
            this.ToFrameId = toFrameId;
            this.Translation = translation;
            this.Rotation = rotation;
            this.TransformSource = transformSource;
            this.Error = error;
        }

        [NotMapped]
        public Vector3 Translation
        {
            get
            {
                return new Vector3(X, Y, Z);
            }
            set
            {
                this.X = value.X;
                this.Y = value.Y;
                this.Z = value.Z;
            }
        }
        
        [NotMapped]
        public Quaternion Rotation
        {
            get
            {
                return new Quaternion(QX, QY, QZ, QW);
            }
            set
            {
                this.QX = value.X;
                this.QY = value.Y;
                this.QZ = value.Z;
                this.QW = value.W;
            }
        }
    }
}
