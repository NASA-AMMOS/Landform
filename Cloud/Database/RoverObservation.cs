using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Cloud
{
    /// <summary>
    /// An observation with extra metadata specific to Mars rovers
    /// </summary>
    public class RoverObservation : Observation
    {
        public int Site { get; set; }
        public int Drive { get; set; }
        public int Version { get; set; }
        public string Sensor { get; set; }
        public string ImageFrameSize { get; set; }
        
        public RoverObservation()
        {

        }

        public RoverObservation(Project project, Frame frame, string name, string url, string observationType, string cameraModel, int site, int drive, int version, string sensor, string imageFrameSize) :
            base(project, frame, name, url, observationType, cameraModel)
        {
            this.Site = site;
            this.Drive = drive;
            this.Version = version;
            this.Sensor = sensor;
            this.ImageFrameSize = imageFrameSize;
        }
    }
}
