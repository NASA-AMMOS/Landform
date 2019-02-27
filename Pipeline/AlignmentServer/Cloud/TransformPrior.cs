using System;
using System.Collections.Generic;
using Amazon.DynamoDBv2.DataModel;
using MathNet.Numerics.LinearAlgebra;
using Newtonsoft.Json;
using OPS.Geometry;

namespace OPS.Pipeline.AlignmentServer
{
    [DynamoDBTable("FrameTransformPriors")]
    public class TransformPrior
    {
        [DynamoDBRangeKey]
        public string ProjectName;

        [DynamoDBHashKey]
        public string Id;

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

        //This constructor must be public for DynamoDb but should not be used
        public TransformPrior() { }

        /// <summary>
        /// Creates a new prior on the value of a transform
        /// </summary>
        protected TransformPrior(string id, Frame frame, UncertainRigidTransform transform)
        {
            this.Id = id;
            this.ProjectName = frame.ProjectName;
            this.FrameName = frame.Name;
            this.Transform = transform;
        }
        
        public static TransformPrior Create(PipelineCore pipeline, Frame frame, UncertainRigidTransform transform)
        {
            TransformPrior tp = new TransformPrior(Guid.NewGuid().ToString(), frame, transform);
            pipeline.SaveDatabaseItem(tp);
            return tp;
        }

        public static TransformPrior Find(PipelineCore pipeline, string project, string id)
        {
            return pipeline.LoadDatabaseItem<TransformPrior>(id, project);
        }

        public static IEnumerable<TransformPrior> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<TransformPrior>("ProjectName", projectName);
        }

        public void Save(PipelineCore pipeline)
        {
            pipeline.SaveDatabaseItem(this);
        }
    }
}
