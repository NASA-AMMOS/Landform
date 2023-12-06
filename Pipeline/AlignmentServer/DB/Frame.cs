using System;
using System.Collections.Generic;

namespace JPLOPS.Pipeline.AlignmentServer
{
    public class Frame
    {
        [DBRangeKey]
        public string ProjectName;

        [DBHashKey]
        public string Name;

        public string ParentName;

        public HashSet<TransformSource> Transforms = new HashSet<TransformSource>(); //MT safety: lock before accessing

        public HashSet<string> ObservationNames = new HashSet<string>(); //MT safety: lock before accessing

        public double EastingMeters; //along equator east of prime meridian

        public double NorthingMeters; //along a meridian north of equator

        public bool HasEastingNorthing;

        public double ElevationMeters;

        public bool HasElevation;

        public double LongitudeDegrees;

        public double LatitudeDegrees;

        public bool HasLonLat;

        public Frame() { }

        /// <summary>
        /// Creates a local instance of a frame.  The frame will have an invalid id
        /// until it is saved to the database.
        /// Frame names must be unique within a project.  If no name is specified a random GUID will used.
        /// </summary>
        /// <param name="projectName"></param>
        /// <param name="name"></param>
        protected Frame(string projectName, string name = null, Frame parent = null) : this()
        {
            this.Name = name ?? Guid.NewGuid().ToString();
            this.ProjectName = projectName;
            this.ParentName = (parent != null) ? parent.Name : null;
        }

        /// <summary>
        /// Creates a frame for the given project with the given name.
        /// If no name is specifed a random GUID will be used.
        /// Saves the frame the the database and returns an object with a valid id.
        /// </summary>
        /// <param name="pipeline"></param>
        /// <param name="projectName"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Frame Create(PipelineCore pipeline, string projectName, string name = null, Frame parent = null, bool save = true)
        {
            Frame f = new Frame(projectName, name, parent);

            if (save)
            {
                f.Save(pipeline);
            }

            return f;
        }

        /// <summary>
        /// Save this observation without overwriting any values it may be missing
        /// </summary>
        /// <param name=""></param>
        public void Save(PipelineCore pipeline)
        {
            pipeline.SaveDatabaseItem(this);
        }

        public void Delete(PipelineCore pipeline, bool ignoreErrors = true)
        {
            pipeline.DeleteDatabaseItem(this, ignoreErrors);
        }

        /// <summary>
        /// Find a frame in the given project with the specififed name.  Create it if it doesn't exist.
        /// Returns the frame if it can be found or created.  Returns null otherwise.
        /// Returned frame is saved in the database and has a valid id.
        /// </summary>
        /// <param name="pipeline"></param>
        /// <param name="projectName"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Frame FindOrCreate(PipelineCore pipeline, string projectName, string name, Frame parent = null)
        {
            Frame frame = Find(pipeline, projectName, name);
            if (frame != null)
            {
                return frame;
            }
            // If it doesn't exist try to create it
            frame = Create(pipeline, projectName, name, parent);
            if (frame != null)
            {
                return frame;
            }
            // If our create failed someone else may have created one between our find and create calls
            // Look for it again.
            return Find(pipeline, projectName, name);
        }

        /// <summary>
        /// Find a frame in the database with the specified project and name.  Returns null if none exists.
        /// </summary>
        public static Frame Find(PipelineCore pipeline, string projectName, string name)
        {
            return pipeline.LoadDatabaseItem<Frame>(name, projectName);
        }

        public static IEnumerable<Frame> Find(PipelineCore pipeline, string projectName)
        {
            foreach (var frame in pipeline.ScanDatabase<Frame>("ProjectName", projectName))
            {
                yield return frame;
            }
        }

        public IEnumerable<Frame> GetChildren(PipelineCore pipeline)
        {
            return pipeline.ScanDatabase<Frame>("ProjectName", ProjectName, "ParentName", Name);
        }

        public Frame GetParent(PipelineCore pipeline)
        {
            if (ParentName == null) return null;
            return Find(pipeline, ProjectName, ParentName);
        }
    }   
}
