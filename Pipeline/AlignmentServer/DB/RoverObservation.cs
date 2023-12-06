using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using JPLOPS.Imaging;

namespace JPLOPS.Pipeline.AlignmentServer
{
    /// <summary>
    /// An observation with extra metadata specific to Mars rovers
    /// </summary>
    public class RoverObservation : Observation
    {
        public int Site;

        public int Drive;

        public RoverProductType ObservationType;

        public RoverProductCamera Camera;

        public RoverProductProducer Producer;

        public RoverProductColor Color;

        //it might be nice to have this field
        //but that would introduce a redundancy with Observation.CameraModel.Linear, so avoiding for now
        //public RoverProductGeometry Geometry;

        [JsonIgnore]
        public SiteDrive SiteDrive { get { return new SiteDrive(Site, Drive); } }

        [JsonIgnore]
        public string StereoFrameName
        {
            get
            {
                var cameraName = Camera.ToString();
                if (FrameName.StartsWith(cameraName) && RoverStereoPair.IsStereo(Camera))
                {
                    return RoverStereoPair.GetStereoCamera(Camera).ToString() + FrameName.Substring(cameraName.Length);
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
                if (RoverStereoPair.IsStereoLeft(Camera))
                {
                    return RoverStereoEye.Left;
                }
                else if (RoverStereoPair.IsStereoRight(Camera))
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
                  Camera != RoverProductCamera.Unknown &&
                  Producer != RoverProductProducer.Unknown))
            {
                throw new Exception("Missing required property in RoverObservation " + Name +
                                    " ObservationType=" + ObservationType +
                                    " Camera=" + Camera +
                                    " Producer=" + Producer);
            }
        }

        public RoverObservation() { }

        protected RoverObservation(Frame frame, string name, string url, CameraModel cameraModel,
                                   bool useForAlignment, bool useForMeshing, bool useForTexturing,
                                   int width, int height, int bands, int bits, int day, int version, int index,
                                   int site, int drive, RoverProductType observationType, RoverProductCamera camera,
                                   RoverProductProducer producer, RoverProductColor color)
            : base(frame, name, url, cameraModel, useForAlignment, useForMeshing, useForTexturing,
                   width, height, bands, bits, day, version, index)
        {
            this.Site = site;
            this.Drive = drive;
            this.ObservationType = observationType;
            this.Camera = camera;
            this.Producer = producer;
            this.Color = color;
            this.IsValidRoverOservation();
        }

        /// <summary>
        /// Prevent possible bugs from calling the default Observation.Create() method.
        /// </summary>
        public static new Observation
            Create(PipelineCore pipeline, Frame frame, string name, string url, CameraModel cameraModel,
                   bool useForAlignment, bool useForMeshing, bool useForTexturing,
                   int width, int height, int bands, int bits, int day, int version, int index, bool save)
        {
            throw new NotImplementedException("Call RoverObservation.Create() with rover specific arguments");
        }

        /// <summary>
        /// Creates a new rover observation and saves it to the database.  Returned observation has a valid id.
        /// Names must be unique within a project.
        /// Project is infered from frame.
        /// </summary>
        public static RoverObservation
            Create(PipelineCore pipeline, Frame frame, string name, string url, CameraModel cameraModel,
                   bool useForAlignment, bool useForMeshing, bool useForTexturing,
                   int width, int height, int bands, int bits, int day, int version, int index,
                   int site, int drive, RoverProductType observationType, RoverProductCamera camera,
                   RoverProductProducer producer, RoverProductColor color, bool save = true)
        {
            if (Find(pipeline, frame.ProjectName, name) != null)
            {
                return null; //An observation with this name and project already exists 
            }
            RoverObservation ro = new RoverObservation(frame, name, url, cameraModel,
                                                       useForAlignment, useForMeshing, useForTexturing,
                                                       width, height, bands, bits, day, version, index,
                                                       site, drive, observationType, camera, producer, color);
            if (save)
            {
                ro.Save(pipeline);
            }
            return ro;
        }

        /// <summary>
        /// overrides Observation.Save() so that pipeline.SaveDatabaseItem() sees the type of the object
        /// as RoverObservation not Observation
        /// </summary>
        public override void Save(PipelineCore pipeline)
        {
            IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        /// <summary>
        /// overrides Observation.Delete() so that pipeline.DeleteDatabaseItem() sees the type of the object
        /// as RoverObservation not Observation
        /// </summary>
        public override void Delete(PipelineCore pipeline, bool ignoreErrors = true)
        {
            pipeline.DeleteDatabaseItem(this, ignoreErrors);
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
            return string.Format("{0}, Site={1}, Drive={2}, ObservationType={3}, Camera={4}, Producer={5}, Color={6}",
                                 base.ToString(brief), Site, Drive, ObservationType, Camera, Producer, Color);
        }

        public override string ToString()
        {
            return ToString(brief: false);
        }
    }
}
