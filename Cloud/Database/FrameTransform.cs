using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Xna.Framework;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2;
using MathNet.Numerics.LinearAlgebra;
using OPS.Geometry;

namespace OPS.Cloud
{
    public static class TransformSource
    {
        public const string Prior = "prior";
        public const string Derived = "derived";
    }

    /// <summary>
    /// Represents the rotation and translation between two frames
    /// Frame transforms are not versioned, so two workers can edit and save them at the same time. 
    /// Frame transform lookups are versioned, but this is internal to the class and workers do not need to worry about it
    /// </summary>
    [DynamoDBTable("FrameTransforms")]
    public class FrameTransform
    {
        [DynamoDBRangeKey]
        [DynamoDBProperty()]
        public string ProjectName { get; set; }

        [DynamoDBHashKey] //Partition key
        [DynamoDBProperty()]
        public string FrameName { get; set; }

        [DynamoDBProperty("Mean", typeof(VectorNConverter))]
        public Vector<double> Mean { get; set; }
        [DynamoDBProperty("Covariance", typeof(SquareMatrixConverter))]
        public Matrix<double> Covariance { get; set; }

        [DynamoDBIgnore]
        public UncertainRigidTransform Transform
        {
            get
            {
                return new UncertainRigidTransform(new MathExtensions.GaussianND(Mean, Covariance));
            }
            set
            {
                Mean = value.Distribution.Mean;
                Covariance = value.Distribution.Covariance;
            }
        }

        //This constructor must be public for DynamoDb but should not be used
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
        protected FrameTransform(Frame frame, UncertainRigidTransform transform, string transformSource)
        {
            this.ProjectName = frame.ProjectName;
            this.FrameName = frame.Name;
            this.Mean = transform.Distribution.Mean;
            this.Covariance = transform.Distribution.Covariance;
        }

        /// <summary>
        /// Creates a transform between two frames and saves it to the database
        /// Saves the lookup for that transform in a lookup entry 
        /// Returns null if the transform already exists or could not be created
        /// </summary>
        /// <param name="context"></param>
        /// <param name="fromFrame"></param>
        /// <param name="toFrame"></param>
        /// <param name="translation"></param>
        /// <param name="rotation"></param>
        /// <param name="transformSource"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public static FrameTransform Create(DynamoDBContext context, Frame frame, UncertainRigidTransform transform, string transformSource)
        {
            //Now that the id has been saved to the lookup table, we are free to add the transform itself 
            FrameTransform ft = new FrameTransform(frame, transform, transformSource);
            context.Save(ft);

            return ft;
        }
        
        public static FrameTransform Find(DynamoDBContext context, Frame frame)
        {
            return context.Load<FrameTransform>(frame.Name, frame.ProjectName);
        }
    }
}
