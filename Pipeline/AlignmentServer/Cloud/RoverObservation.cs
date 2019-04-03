using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Newtonsoft.Json;
using OPS.Cloud;

namespace OPS.Pipeline.AlignmentServer
{
    /// <summary>
    /// An observation with extra metadata specific to Mars rovers
    /// </summary>
    [DynamoDBTable("Observations")]
    [DynamoDBReadCapacity(50, 100)]
    [DynamoDBWriteCapacity(50, 100)]
    public class RoverObservation : Observation
    {
        public int Site;

        public int Drive;

        public string Version;

        public string Sensor;

        public string ImageFrameSize;

        public string Producer;

        [DynamoDBIgnore]
        [JsonIgnore]
        public SiteDrive SiteDrive { get { return new SiteDrive(Site, Drive); } }

        [DynamoDBIgnore]
        [JsonIgnore]
        public bool IsMastcam
        {
            get
            {
                return Sensor == RoverProductCamera.MastcamLeft.ToString() ||
                    Sensor == RoverProductCamera.MastcamRight.ToString();
            }
        }
      
        protected void IsValidRoverOservation()
        {
            base.IsValid();
            if (!(Version != null &&
                  Sensor != null &&
                  ImageFrameSize != null &&
                  Producer != null))
            {
                throw new Exception("Missing required property in RoverObservation " + Name +
                                    " Version=" + Version +
                                    " Sensor=" + Sensor +
                                    " ImageFrameSize=" + ImageFrameSize +
                                    " Producer=" + Producer);
            }
        }

        //This constructor must be public for DynamoDb but should not be used
        public RoverObservation() { }

        protected RoverObservation(Frame frame, string name, string url, string observationType, string cameraModel, bool useForReconstruction, int site, int drive, string version, string sensor, string imageFrameSize, string producer, int width, int height) :
            base(frame, name, url, observationType, cameraModel, useForReconstruction, width, height)
        {
            this.Site = site;
            this.Drive = drive;
            this.Version = version;
            this.Sensor = sensor;
            this.ImageFrameSize = imageFrameSize;
            this.Producer = producer;
            this.IsValidRoverOservation();
        }

        /// <summary>
        /// Prevent possible bugs from calling the default Observation.Create method.
        /// </summary>
        /// <param name="pipeline"></param>
        /// <param name="frame"></param>
        /// <param name="name"></param>
        /// <param name="url"></param>
        /// <param name="observationType"></param>
        /// <param name="cameraModel"></param>
        /// <param name="useForReconstruction"></param>
        /// <returns></returns>
        public static Observation Create(PipelineCore pipeline, Frame frame, string name, string url, string observationType, string cameraModel, bool useForReconstruction)
        {
            throw new NotImplementedException("Call the other version of RoverObservation.Create with rover specific arguments");
        }

        /// <summary>
        /// Creates a new rover observation and saves it to the database.  Returned observation has a valid id.
        /// Names must be unique within a project.
        /// Project is infered from frame.
        /// </summary>
        /// <param name="pipeline"></param>
        /// <param name="frame"></param>
        /// <param name="name"></param>
        /// <param name="url"></param>
        /// <param name="observationType"></param>
        /// <param name="cameraModel"></param>
        /// <returns></returns>
        public static RoverObservation Create(PipelineCore pipeline, Frame frame, string name, string url, string observationType, string cameraModel, bool useForReconstruction, int site, int drive, string version, string sensor, string imageFrameSize, string producer, int width, int height)
        {
            if (Find(pipeline, frame.ProjectName, name) != null)
            {
                return null; //An observation with this name and project already exists 
            }
            RoverObservation ro = new RoverObservation(frame, name, url, observationType, cameraModel, useForReconstruction, site, drive, version, sensor, imageFrameSize, producer, width, height);
            pipeline.SaveDatabaseItem(ro);
            return ro;
        }

        public override void Save(PipelineCore pipeline)
        {
            pipeline.SaveDatabaseItem(this);
        }

        /// <summary>
        /// Finds an observation based on its name and project
        /// Return null if observation cannot be found
        /// </summary>
        /// <param name="pipeline"></param>
        /// <param name="imageId"></param>
        /// <returns></returns>
        new public static RoverObservation Find(PipelineCore pipeline, string projectName, string name)
        {
            return pipeline.LoadDatabaseItem<RoverObservation>(name, projectName);
        }

        new public static IEnumerable<RoverObservation> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<RoverObservation>("ProjectName", projectName);
        }

        new public static IEnumerable<RoverObservation> Find(PipelineCore pipeline, Frame frame)
        {
            //return pipeline.ScanDatabase<RoverObservation>("ProjectName", frame.ProjectName, "FrameName", frame.Name);
            foreach (var obsName in frame.ObservationNames)
            {
                yield return Find(pipeline, frame.ProjectName, obsName);
            }
        }
    }
}
