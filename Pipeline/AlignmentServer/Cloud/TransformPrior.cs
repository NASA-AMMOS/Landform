using Amazon.DynamoDBv2.DataModel;
using MathNet.Numerics.LinearAlgebra;
using OPS.Geometry;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public TransformPrior()
        {

        }

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
    }
}
