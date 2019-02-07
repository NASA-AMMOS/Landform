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
        [DynamoDBProperty()]
        public string ProjectName { get; set; }

        [DynamoDBHashKey]
        [DynamoDBProperty]
        public string Id { get; set; }

        [DynamoDBProperty()]
        public string FrameName { get; set; }

        [DynamoDBProperty("Mean", typeof(VectorNConverter))]
        [JsonConverter(typeof(VectorNConverter))]
        public Vector<double> Mean { get; set; }

        [DynamoDBProperty("Covariance", typeof(SquareMatrixConverter))]
        [JsonConverter(typeof(SquareMatrixConverter))]
        public Matrix<double> Covariance { get; set; }

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
            TransformPrior ft = new TransformPrior(Guid.NewGuid().ToString(), frame, transform);
            pipeline.SaveDatabaseItem(ft);
            return ft;
        }

        public static TransformPrior Find(PipelineCore pipeline, string project, string id)
        {
            return pipeline.LoadDatabaseItem<TransformPrior>(id, project);
        }

        public void Save(PipelineCore pipeline)
        {
            pipeline.SaveDatabaseItem(this);
        }
    }
}
