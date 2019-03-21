using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Amazon.DynamoDBv2.DataModel;
using OPS.Cloud;
using OPS.Geometry;

namespace OPS.Pipeline.AlignmentServer
{
    /// <summary>
    /// Represents a coordinate frame in the database
    /// Coordinate frames can have one or more observations associated with them. 
    /// Frames are not versioned
    /// </summary>\
    [DynamoDBTable("Frames")]
    [DynamoDBReadCapacity(5, 50)]
    [DynamoDBWriteCapacity(5, 50)]
    public class Frame
    {
        [DynamoDBRangeKey]
        public string ProjectName;

        [DynamoDBHashKey]
        public string Name;

        public string ParentName;

        public List<TransformSource> Transforms;

        public List<string> ObservationNames;

        //DEPRECATED - for legacy compat only
        public string FrameName;

        //DEPRECATED - for legacy compat only
        public List<string> PriorIds;

        //This constructor must be public for DynamoDB but should not be used
        public Frame()
        {
            Transforms = new List<TransformSource>();
            ObservationNames = new List<string>();
        }

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
        public static Frame Create(PipelineCore pipeline, string projectName, string name = null, Frame parent = null)
        {
            Frame f = new Frame(projectName, name, parent);
            f.Save(pipeline);
            return f;
        }

        /// <summary>
        /// Save this observation without overwriting any values it may be missing
        /// </summary>
        /// <param name=""></param>
        public void Save(PipelineCore pipeline)
        {
            Transforms = (new HashSet<TransformSource>(Transforms)).ToList();
            ObservationNames = (new HashSet<string>(ObservationNames)).ToList();
            pipeline.SaveDatabaseItem(this);
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
            return Compat(pipeline, pipeline.LoadDatabaseItem<Frame>(name, projectName));
        }

        public static IEnumerable<Frame> Find(PipelineCore pipeline, string projectName)
        {
            foreach (var frame in pipeline.ScanDatabase<Frame>("ProjectName", projectName))
            {
                yield return Compat(pipeline, frame);
            }
        }

        public bool AddTransform(FrameTransform transform)
        {
            return AddTransform(transform.Source);
        }

        public bool AddTransform(TransformSource transformSource)
        {
            if (Transforms.Contains(transformSource))
            {
                return false;
            }
            Transforms.Add(transformSource);
            return true;
        }

        public bool RemoveTransform(FrameTransform transform)
        {
            return RemoveTransform(transform.Source);
        }

        public bool RemoveTransform(TransformSource transformSource)
        {
            if (!Transforms.Contains(transformSource))
            {
                return false;
            }
            Transforms.Remove(transformSource);
            return true;
        }

        public bool AddObservation(Observation observation)
        {
            if (ObservationNames.Contains(observation.Name))
            {
                return false;
            }
            ObservationNames.Add(observation.Name);
            return true;
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

        private static Dictionary<string, List<FrameTransform>> compatTransformCache = null;
        private static ObservationCache compatObservationCache = null;
        private static object compatLock = new Object();
        private static Frame Compat(PipelineCore pipeline, Frame frame)
        {
            if (pipeline.LegacyCompat)
            {
                lock (compatLock)
                {
                    if (string.IsNullOrEmpty(frame.Name))
                    {
                        frame.Name = frame.FrameName;
                    }
                    
                    if (frame.Transforms.Count == 0)
                    {
                        if (compatTransformCache == null)
                        {
                            pipeline.LogInfo("populating legacy compat transform cache...");
                            compatTransformCache = new Dictionary<string, List<FrameTransform>>();
                            
                            foreach (var ft in pipeline.ScanDatabase<FrameTransform>())
                            {
                                if (!compatTransformCache.ContainsKey(ft.FrameName))
                                {
                                    compatTransformCache[ft.FrameName] = new List<FrameTransform>();
                                }
                                ft.Source = TransformSource.Adjusted; //this should be the default anyway
                                compatTransformCache[ft.FrameName].Add(ft);
                            }
                            
                            foreach (var ft in pipeline.ScanDatabase<FrameTransform>(null, tableName: "FrameTransformPriors"))
                            {
                                if (!compatTransformCache.ContainsKey(ft.FrameName))
                                {
                                    compatTransformCache[ft.FrameName] = new List<FrameTransform>();
                                }
                                ft.Source = TransformSource.Prior;
                                compatTransformCache[ft.FrameName].Add(ft);
                            }
                        }
                        var transformsForFrame = compatTransformCache[frame.Name];
                        if (transformsForFrame != null)
                        {
                            foreach (var transform in transformsForFrame)
                            {
                                frame.AddTransform(transform);
                            }
                        }
                        if (string.IsNullOrEmpty(frame.ParentName))
                        {
                            //root frame doesn't have a prior in the legacy database, but it's just identity
                            frame.AddTransform(new FrameTransform(frame, TransformSource.Prior,
                                                                  new UncertainRigidTransform()));
                        }
                    }
                    
                    if (frame.ObservationNames.Count == 0)
                    {
                        if (compatObservationCache == null)
                        {
                            pipeline.LogInfo("populating legacy compat observation cache...");
                            compatObservationCache = new ObservationCache(pipeline, frame.ProjectName);
                            compatObservationCache.Preload();
                        }
                        foreach (var obs in compatObservationCache.GetAllObservationsForFrame(frame))
                        {
                            frame.AddObservation(obs);
                        }
                    }
                }
            }

            return frame;
        }
    }   
}
