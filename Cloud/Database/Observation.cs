using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DataModel;

namespace OPS.Cloud
{
    /// <summary>
    /// Represents an image or 3D shape measurement of the environment
    /// Can be connected to Frames and aligned with other observations through FrameTransforms
    /// Observations are not versioned, because all of the data associated with them is deterministic, so it does not matter if workers re-upload them. 
    /// Fresh Creates, or Saves with missing values, will not overwrite existing values. 
    /// </summary>
    [DynamoDBTable("Observations")]
    [DynamoDBReadCapacity(5, 50)]
    [DynamoDBWriteCapacity(5, 50)]
    public class Observation
    {
        [DynamoDBRangeKey]
        public string ProjectName { get; set; }

        [DynamoDBHashKey]
        public string Name { get; set; }

        public string Url { get; set; }

        public Guid FeaturesGuid { get; set; }

        public Guid MaskGuid { get; set; }

        public string FeatureUrl { get; set; }

        public string FrameName { get; set; }

        public string ObservationType { get; set; }

        public string CameraModel { get; set; }

        public bool UseForReconstruction { get; set; }

        /// Add required fields here 
        private void IsValid()
        {
            if (!(Url != null &&
                FrameName != null &&
                ProjectName != null &&
                Name != null &&
                ObservationType != null))
            {
                throw new CloudException("Missing required property in Observation");
            }
        }

        //This constructor must be public for DynamoDb but should not be used
        public Observation()
        {
            
        }

        /// <summary>
        /// Creates a new local observation object.  
        /// Observation names must be unique within a project.
        /// ProjectId for this observation will be inferred from the supplied Frame object.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="frame"></param>
        /// <param name="url"></param>
        /// <param name="observationType"></param>
        /// <param name="cameraModel"></param>
        protected Observation(Frame frame, string name, string url, string observationType, string cameraModel, bool useForReconstruction)
        {
            this.ProjectName = frame.ProjectName;
            this.FrameName = frame.Name;
            this.Name = name;
            this.Url = url;
            this.ObservationType = observationType;
            this.CameraModel = cameraModel;
            this.UseForReconstruction = useForReconstruction;
            IsValid();
        }



        /// <summary>
        /// Creates a new observation and saves it to the database.  Returned observation has a valid id.
        /// Names must be unique within a project.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="frame"></param>
        /// <param name="name"></param>
        /// <param name="url"></param>
        /// <param name="observationType"></param>
        /// <param name="cameraModel"></param>
        /// <returns></returns>
        public static Observation Create(DynamoDBContext context, Frame frame, string name, string url, string observationType, string cameraModel, bool useForReconstruction)
        {
            Observation obs = new Observation(frame, name, url, observationType, cameraModel, useForReconstruction);
            context.Save(obs, new DynamoDBOperationConfig { IgnoreNullValues = true });
            return obs;
        }

        /// <summary>
        /// Save this observation without overwriting any values it may be missing
        /// </summary>
        /// <param name=""></param>
        public void Save(DynamoDBContext context)
        {
            context.Save(this, new DynamoDBOperationConfig { IgnoreNullValues = true });
        }

        /// <summary>
        /// Finds an observation based on its name and project
        /// Return null if observation cannot be found
        /// </summary>
        /// <param name="context"></param>
        /// <param name="imageId"></param>
        /// <returns></returns>
        public static Observation Find(DynamoDBContext context, string projectName, string name)
        {
            return context.Load<Observation>(name, projectName);
        }

        public static IEnumerable<Observation> Find(DynamoDBContext context, Frame frame)
        {
            return context.Scan<Observation>(
                new ScanCondition("ProjectName", Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, frame.ProjectName),
                new ScanCondition("FrameName", Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, frame.Name)
                );
        }

        public static IEnumerable<Observation> FindByType(DynamoDBContext context, string projectName, string observationType)
        {
            return context.Scan<Observation>(
                new ScanCondition("ProjectName", Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, projectName),
                new ScanCondition("ObservationType", Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, observationType)
                );
        }
    }
}
