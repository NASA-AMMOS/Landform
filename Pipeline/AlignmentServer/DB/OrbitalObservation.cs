using System;
using System.Collections.Generic;
using Amazon.DynamoDBv2.DataModel;
using Newtonsoft.Json;
using OPS.Cloud;
using OPS.Imaging;

namespace OPS.Pipeline.AlignmentServer
{
    /// <summary>
    /// An observation with extra metadata specific to Mars rovers
    /// </summary>
    [DynamoDBTable("Observations")]
    [DynamoDBReadCapacity(50, 100)]
    [DynamoDBWriteCapacity(50, 100)]
    public class OrbitalObservation : Observation
    {
        public OrbitalObservation() { }

        protected OrbitalObservation(Frame frame, string name, string url, GISCameraModel cameraModel, 
                                   bool useForAlignment, bool useForMeshing, bool useForTexturing)
            : base(frame, name, url, cameraModel, useForAlignment, useForMeshing, useForTexturing,
                   cameraModel.Width, cameraModel.Height, 1, 8, 0, 0, Observation.ORBITAL_INDEX)
        {

        }

        public static OrbitalObservation Create(Frame frame, string name, string url, GISCameraModel cameraModel,
                                   bool useForAlignment, bool useForMeshing, bool useForTexturing)
        {
            return new OrbitalObservation(frame, name, url, cameraModel,
                                    useForAlignment, useForMeshing, useForTexturing);
        }
        // <summary>
        /// Prevent possible bugs from calling the default Observation.Create() method.
        /// </summary>
        public static new Observation
            Create(PipelineCore pipeline, Frame frame, string name, string url, CameraModel cameraModel,
                   bool useForAlignment, bool useForMeshing, bool useForTexturing,
                   int width, int height, int bands, int bits, int day, int version, int index, bool save)
        {
            throw new NotImplementedException("Call OrbitalObservation.Create() with orbital specific arguments");
        }
        /// <summary>
        /// overrides Observation.Save() so that pipeline.SaveDatabaseItem() sees the type of the object
        /// as RoverObservation not Observation
        /// </summary>
        public override void Save(PipelineCore pipeline)
        {
            IsValid();
            //pipeline.SaveDatabaseItem(this);
        }

        /// <summary>
        /// overrides Observation.Delete() so that pipeline.DeleteDatabaseItem() sees the type of the object
        /// as RoverObservation not Observation
        /// </summary>
        public override void Delete(PipelineCore pipeline, bool ignoreErrors = true)
        {
            //pipeline.DeleteDatabaseItem(this, ignoreErrors);
        }

        /// <summary>
        /// Finds an observation based on its name and project
        /// Return null if observation cannot be found
        /// </summary>
        new public static OrbitalObservation Find(PipelineCore pipeline, string projectName, string name)
        {
            return null;
        }

        public override string ToString(bool brief)
        {
            //return string.Format("{0}, Site={1}, Drive={2}, ObservationType={3}, Camera={4}, Producer={5}, Color={6}",
            //                     base.ToString(brief), Site, Drive, ObservationType, Camera, Producer, Color);

            return "Orbital";
        }

        public override string ToString()
        {
            return ToString(brief: false);
        }
    }
}