using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Newtonsoft.Json;
using OPS.Cloud;
using OPS.Pipeline;

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

        public RoverProductType ObservationType;

        public RoverProductCamera Sensor;

        public RoverProductProducer Producer;

        //it might be nice to have this field
        //but that would introduce a redundancy with Observation.CameraModel.Linear, so avoiding for now
        //public RoverProductGeometry Geometry;

        [DynamoDBIgnore]
        [JsonIgnore]
        public SiteDrive SiteDrive { get { return new SiteDrive(Site, Drive); } }

        [DynamoDBIgnore]
        [JsonIgnore]
        public string StereoFrameName
        {
            get
            {
                var cameraName = Sensor.ToString();
                if (FrameName.StartsWith(cameraName) && RoverStereoPair.IsStereo(Sensor))
                {
                    return RoverStereoPair.GetStereoCamera(Sensor).ToString() + FrameName.Substring(cameraName.Length);
                }
                else
                {
                    return FrameName;
                }
            }
        }

        public RoverStereoEye StereoEye
        {
            get
            {
                if (RoverStereoPair.IsStereoLeft(Sensor))
                {
                    return RoverStereoEye.Left;
                }
                else if (RoverStereoPair.IsStereoRight(Sensor))
                {
                    return RoverStereoEye.Right;
                }
                else
                {
                    return RoverStereoEye.Mono;
                }
            }
        }
      
        protected void IsValidRoverOservation()
        {
            base.IsValid();
            if (!(ObservationType != RoverProductType.Unknown &&
                  Sensor != RoverProductCamera.Unknown &&
                  Producer != RoverProductProducer.Unknown))
            {
                throw new Exception("Missing required property in RoverObservation " + Name +
                                    " ObservationType=" + ObservationType +
                                    " Sensor=" + Sensor +
                                    " Producer=" + Producer);
            }
        }

        //This constructor must be public for DynamoDb but should not be used
        public RoverObservation() { }

        protected RoverObservation(Frame frame, string name, string url, RoverProductType observationType,
                                   string cameraModel, bool useForReconstruction, int site, int drive, RoverProductCamera sensor,
                                   RoverProductProducer producer, int width, int height, int bands, int bits,
                                   int day, int version, int index) :
            base(frame, name, url, cameraModel, useForReconstruction, width, height, bands, bits, day,
                 version, index)
        {
            this.Site = site;
            this.Drive = drive;
            this.ObservationType = observationType;
            this.Sensor = sensor;
            this.Producer = producer;
            this.IsValidRoverOservation();
        }

        /// <summary>
        /// Prevent possible bugs from calling the default Observation.Create() method.
        /// </summary>
        public static new Observation Create(PipelineCore pipeline, Frame frame, string name, string url,
                                             string cameraModel, bool useForReconstruction,
                                             int width, int height, int bands, int bits, int day, int version,
                                             int index, bool save)
        {
            throw new NotImplementedException("Call RoverObservation.Create() with rover specific arguments");
        }

        /// <summary>
        /// Creates a new rover observation and saves it to the database.  Returned observation has a valid id.
        /// Names must be unique within a project.
        /// Project is infered from frame.
        /// </summary>
        public static RoverObservation Create(PipelineCore pipeline, Frame frame, string name, string url,
                                              RoverProductType observationType, string cameraModel,bool useForReconstruction,
                                              int site, int drive, RoverProductCamera sensor,
                                              RoverProductProducer producer, int width, int height, int bands,
                                              int bits, int day, int version, int index, bool save = true)
        {
            if (Find(pipeline, frame.ProjectName, name) != null)
            {
                return null; //An observation with this name and project already exists 
            }
            RoverObservation ro =
                new RoverObservation(frame, name, url, observationType, cameraModel, useForReconstruction, site, drive,
                                     sensor, producer, width, height, bands, bits, day, version, index);
            if (save)
            {
                pipeline.SaveDatabaseItem(ro);
            }
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

        public override string ToString(bool brief)
        {
            return string.Format("{0}, Site={1}, Drive={2}, ObservationType={3}, Sensor={4}, Producer={5}",
                                 base.ToString(brief), Site, Drive, ObservationType, Sensor, Producer);
        }

        public override string ToString()
        {
            return ToString(brief: false);
        }
    }
}
