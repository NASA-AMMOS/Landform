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
using Newtonsoft.Json;
using OPS.Geometry;
using OPS.Cloud;

namespace OPS.Pipeline.AlignmentServer
{
    /// <summary>
    /// Represents the rotation and translation between two frames
    /// Frame transforms are not versioned, so two workers can edit and save them at the same time. 
    /// Frame transform lookups are versioned, but this is internal to the class
    /// </summary>
    [DynamoDBTable("FrameTransforms")]
    [DynamoDBReadCapacity(5, 50)]
    [DynamoDBWriteCapacity(5, 50)]
    public class FrameTransform
    {
        [DynamoDBRangeKey]
        public string ProjectName;

        [DynamoDBHashKey]
        public string FrameName;

        [DynamoDBProperty("Mean", typeof(VectorNConverter))]
        [JsonConverter(typeof(VectorNConverter))]
        public Vector<double> Mean;

        [DynamoDBProperty("Covariance", typeof(SquareMatrixConverter))]
        [JsonConverter(typeof(SquareMatrixConverter))]
        public Matrix<double> Covariance;

        [DynamoDBIgnore]
        [JsonIgnore]
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

        //This constructor must be public for DynamoDB but should not be used
        public FrameTransform() { }

        /// <summary>
        /// Creates a new transform specifying the relationship between two frames
        /// </summary>
        /// <param name="fromFrame"></param>
        /// <param name="toFrame"></param>
        /// <param name="translation"></param>
        /// <param name="rotation"></param>
        /// <param name="transformSource"></param>
        /// <param name="error"></param>
        protected FrameTransform(Frame frame, UncertainRigidTransform transform)
        {
            this.ProjectName = frame.ProjectName;
            this.FrameName = frame.Name;
            this.Mean = transform.Distribution.Mean;
            this.Covariance = transform.Distribution.Covariance;
        }
        
        public static FrameTransform Create(PipelineCore pipeline, Frame frame, UncertainRigidTransform transform)
        {
            //Now that the id has been saved to the lookup table, we are free to add the transform itself 
            FrameTransform ft = new FrameTransform(frame, transform);
            pipeline.SaveDatabaseItem(ft);

            return ft;
        }

        /// <summary>
        /// Save this transform without overwriting any values it may be missing
        /// </summary>
        /// <param name=""></param>
        public void Save(PipelineCore pipeline)
        {
            pipeline.SaveDatabaseItem(this);
        }

        public static FrameTransform FindOrCreate(PipelineCore pipeline, Frame frame, UncertainRigidTransform transform)
        {
            // Try to find this project
            FrameTransform frameTransform = Find(pipeline, frame);
            if (frameTransform != null)
            {
                return frameTransform;
            }
            // If it doesn't exist try to create it
            frameTransform = Create(pipeline, frame, transform);
            if (frameTransform != null)
            {
                return frameTransform;
            }
            // If our create failed someone else may have created one between our find and create calls
            // Look for it again.
            return Find(pipeline, frame);
        }

        public static IEnumerable<FrameTransform> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<FrameTransform>("ProjectName", projectName);
        }

        public static FrameTransform Find(PipelineCore pipeline, string projectName, string frameName)
        {
            return pipeline.LoadDatabaseItem<FrameTransform>(frameName, projectName);
        }

        public static FrameTransform Find(PipelineCore pipeline, Frame frame)
        {
            return Find(pipeline, frame.ProjectName, frame.Name);
        }
    }
}
