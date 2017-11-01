using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.Infrastructure;
using Amazon.DynamoDBv2.DataModel;

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
        public int ProjectId { get; set; } //Why is this not a reference??
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

        /// <summary>
        /// Creates a new transform specifying the relationship between two frames
        /// </summary>
        /// <param name="fromFrame"></param>
        /// <param name="toFrame"></param>
        /// <param name="translation"></param>
        /// <param name="rotation"></param>
        /// <param name="transformSource"></param>
        /// <param name="error"></param>
        protected FrameTransform(Frame fromFrame, Frame toFrame, Vector3 translation, Quaternion rotation, string transformSource, double error)
        {
            if(!fromFrame.HasValidId() || !toFrame.HasValidId())
            {
                throw new CloudException("Invalid frame id in frame transform.  fromFrame and toFrame must have been saved to database");
            }
            if(fromFrame.ProjectId != toFrame.ProjectId)
            {
                throw new CloudException("Frame transform fromFrame and toFrame must be in the same project");
            }
            if(fromFrame.ProjectId == 0)
            {
                throw new CloudException("Invalid project id for frame transform");
            }
            this.ProjectId = fromFrame.ProjectId;
            this.FromFrameId = fromFrame.Id;
            this.ToFrameId = toFrame.Id;
            this.Translation = translation;
            this.Rotation = rotation;
            this.TransformSource = transformSource;
            this.Error = error;
        }

        /// <summary>
        /// Creates a transform between two frames and saves it to the database
        /// Returns the transform with a valid id
        /// Returns null if the transform could not be created
        /// </summary>
        /// <param name="context"></param>
        /// <param name="fromFrame"></param>
        /// <param name="toFrame"></param>
        /// <param name="translation"></param>
        /// <param name="rotation"></param>
        /// <param name="transformSource"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        //public static FrameTransform Create(DynamoDBContext context, Frame fromFrame, Frame toFrame, Vector3 translation, Quaternion rotation, string transformSource, double error)
        //{
        //    try
        //    {
        //        FrameTransform transform = context.FrameTransforms.Add(new FrameTransform(fromFrame, toFrame, translation, rotation, transformSource, error));
        //        context.SaveChanges();
        //        return transform;
        //    }
        //    catch (DbUpdateException)
        //    {
        //        // A record with this unique name and project id combination already exists
        //    }
        //    return null;
        //}

            /*
        /// <summary>
        /// Find all FrameTransforms that map fromFrame->toFrame
        /// </summary>
        /// <param name="context"></param>
        /// <param name="fromFrame"></param>
        /// <param name="toFrame"></param>
        /// <returns></returns>
        public static IEnumerable<FrameTransform> Find(LandformDbContext context, Frame fromFrame, Frame toFrame)
        {
            return context.FrameTransforms.Where(f => f.FromFrameId == fromFrame.Id && f.ToFrameId == toFrame.Id);
        }*/

        [DynamoDBIgnore]
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
        
        [DynamoDBIgnore]
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
