using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DataModel;

namespace OPS.Cloud
{
    /// <summary>
    /// An observation with extra metadata specific to Mars rovers
    /// </summary>
    public class RoverObservation : Observation
    {
        public int Site { get; set; }
        public int Drive { get; set; }
        public string Version { get; set; }
        public string Sensor { get; set; }
        public string ImageFrameSize { get; set; }
        public string Producer { get; set; }

        //This constructor must be public for DynamoDb but should not be used
        public RoverObservation()
        {
           
        }

        protected RoverObservation(Frame frame, string name, string url, string observationType, string cameraModel, bool useForReconstruction, int site, int drive, string version, string sensor, string imageFrameSize, string producer, int width, int height) :
            base(frame, name, url, observationType, cameraModel, useForReconstruction, width, height)
        {
            this.Site = site;
            this.Drive = drive;
            this.Version = version;
            this.Sensor = sensor;
            this.ImageFrameSize = imageFrameSize;
            this.Producer = producer;
        }

        /// <summary>
        /// Prevent possible bugs from calling the default Observation.Create method.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="frame"></param>
        /// <param name="name"></param>
        /// <param name="url"></param>
        /// <param name="observationType"></param>
        /// <param name="cameraModel"></param>
        /// <param name="useForReconstruction"></param>
        /// <returns></returns>
        public static Observation Create(DynamoDBContext context, Frame frame, string name, string url, string observationType, string cameraModel, bool useForReconstruction)
        {
            throw new NotImplementedException("Call the other version of RoverObservation.Create with rover specific arguments");
        }

        /// <summary>
        /// Creates a new rover observation and saves it to the database.  Returned observation has a valid id.
        /// Names must be unique within a project.
        /// Project is infered from frame.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="frame"></param>
        /// <param name="name"></param>
        /// <param name="url"></param>
        /// <param name="observationType"></param>
        /// <param name="cameraModel"></param>
        /// <returns></returns>
        public static RoverObservation Create(DynamoDBContext context, Frame frame, string name, string url, string observationType, string cameraModel, bool useForReconstruction, int site, int drive, string version, string sensor, string imageFrameSize, string producer, int width, int height)
        {
            if (Observation.Find(context, frame.ProjectName, name) != null)
            {
                return null; //An observation with this name and project already exists 
            }
            RoverObservation ro = new RoverObservation(frame, name, url, observationType, cameraModel, useForReconstruction, site, drive, version, sensor, imageFrameSize, producer, width, height);
            context.Save(ro);
            return ro;
        }

        /// <summary>
        /// Finds an observation based on its name and project
        /// Return null if observation cannot be found
        /// </summary>
        /// <param name="context"></param>
        /// <param name="imageId"></param>
        /// <returns></returns>
        new public static RoverObservation Find(DynamoDBContext context, string projectName, string name)
        {
            return context.Load<RoverObservation>(name, projectName);
        }

        new public static IEnumerable<RoverObservation> Find(DynamoDBContext context, string projectName)
        {
            return context.Scan<RoverObservation>(
                new ScanCondition("ProjectName", Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, projectName)
                );
        }

        new public static IEnumerable<RoverObservation> Find(DynamoDBContext context, Frame frame)
        {
            return context.Scan<RoverObservation>(
                new ScanCondition("ProjectName", Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, frame.ProjectName),
                new ScanCondition("FrameName", Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, frame.Name)
                );
        }
    }
}
