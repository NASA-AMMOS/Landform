using System;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure;
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
        
        public RoverObservation()
        {

        }

        protected RoverObservation(Frame frame, string name, string url, string observationType, string cameraModel, bool useForReconstruction, int site, int drive, string version, string sensor, string imageFrameSize) :
            base(frame, name, url, observationType, cameraModel, useForReconstruction)
        {
            this.Site = site;
            this.Drive = drive;
            this.Version = version;
            this.Sensor = sensor;
            this.ImageFrameSize = imageFrameSize;
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
        new public static Observation Create(DynamoDBContext context, Frame frame, string name, string url, string observationType, string cameraModel, bool useForReconstruction)
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
        public static RoverObservation Create(DynamoDBContext context, Frame frame, string name, string url, string observationType, string cameraModel, bool useForReconstruction, int site, int drive, string version, string sensor, string imageFrameSize)
        {
            RoverObservation ro = new RoverObservation(frame, name, url, observationType, cameraModel, useForReconstruction, site, drive, version, sensor, imageFrameSize);
            ro.Id = 1; 
            context.Save(ro);
            return ro;
        }
    }
}
