using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using Newtonsoft.Json;
using JPLOPS.Geometry;


namespace JPLOPS.Pipeline.AlignmentServer
{
    public enum TransformSource
    {
        //these are totally ordered by priority, highest priorty first

        Adjusted = 0, //general adjusted transform
        Manual = 10, //manually adjusted
        Landform = 19, //Landform bundle adjusted
        LandformHeightmap = 20, //Landform heightmap aligned
        LandformBEV = 21, //Landform birds eye view aligned
        LandformBEVRoot = 22, //Landform birds eye view root
        LandformBEVCalf = 23, //Landform birds eye view calf
        Agisoft = 30, //Agisoft bundle adjusted

        Prior = 100, //general prior transform
        LegacyManifest = 105, //prior from legacy onsight manifest
        PlacesDB = 110, //prior from mission "places" databsae
        LocationsDB = 120, //prior from mission "locations" database
        PlacesDBSitePDSLocal = 125, //site to origin from places database, local_level to site from PDS header
        PDSChained = 128, //prior relative to first site in project from chained PDS headers
        PDS = 130 //prior relative to parent site from PDS header
    }

    /// <summary>
    /// Represents the rotation and translation between two frames
    /// Frame transforms are not versioned, so two workers can edit and save them at the same time. 
    /// Frame transform lookups are versioned, but this is internal to the class
    /// </summary>
    public class FrameTransform
    {
        [DBRangeKey]
        public string ProjectName;

        [DBHashKey]
        public string Name;

        public string FrameName;

        public TransformSource Source;

        [JsonConverter(typeof(VectorNConverter))]
        public Vector<double> Mean;

        [JsonConverter(typeof(SquareMatrixConverter))]
        public Matrix<double> Covariance;

        [JsonIgnore]
        public UncertainRigidTransform Transform
        {
            get
            {
                return new UncertainRigidTransform(Mean, Covariance);
            }
            set
            {
                Mean = value.Distribution.Mean;
                Covariance = value.Distribution.Covariance;
            }
        }

        public FrameTransform() { }

        /// <summary>
        /// Creates a new transform specifying the relationship between two frames
        /// </summary>
        public FrameTransform(Frame frame, TransformSource source, UncertainRigidTransform transform)
        {
            this.ProjectName = frame.ProjectName;
            this.Name = MakeName(frame.Name, source);
            this.FrameName = frame.Name;
            this.Source = source;
            this.Mean = transform.Distribution.Mean;
            this.Covariance = transform.Distribution.Covariance;
        }

        public static string MakeName(string frameName, TransformSource source)
        {
            return string.Format("{0}-{1}", frameName, source);
        }

        public static bool SplitName(string name, out string frameName, out TransformSource source)
        {
            frameName = null;
            source = TransformSource.Prior;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            int dash = name.IndexOf('-');
            if (dash < 0 || dash == name.Length - 1)
            {
                return false;
            }
            frameName = name.Substring(0, dash);
            return Enum.TryParse(name.Substring(dash + 1), true, out source);
        }

        public static FrameTransform Create(PipelineCore pipeline, Frame frame, TransformSource source,
                                            UncertainRigidTransform transform)
        {
            FrameTransform ft = new FrameTransform(frame, source, transform);
            pipeline.SaveDatabaseItem(ft);
            return ft;
        }

        public static FrameTransform FindOrCreate(PipelineCore pipeline, Frame frame, TransformSource source,
                                                  UncertainRigidTransform transform)
        {
            FrameTransform frameTransform = Find(pipeline, frame, source);
            if (frameTransform != null)
            {
                return frameTransform;
            }

            // If it doesn't exist try to create it
            frameTransform = Create(pipeline, frame, source, transform);
            if (frameTransform != null)
            {
                return frameTransform;
            }

            // If our create failed someone else may have created one between our find and create calls
            // Look for it again.
            return Find(pipeline, frame, source);
        }

        /// <summary>
        /// Save this transform without overwriting any values it may be missing
        /// </summary>
        public void Save(PipelineCore pipeline)
        {
            pipeline.SaveDatabaseItem(this);
        }

        public void Delete(PipelineCore pipeline, bool ignoreErrors = true)
        {
            pipeline.DeleteDatabaseItem(this, ignoreErrors);
        }

        public static IEnumerable<FrameTransform> Find(PipelineCore pipeline, string projectName)
        {
            foreach (var ft in pipeline.ScanDatabase<FrameTransform>("ProjectName", projectName))
            {
                yield return ft;
            }
        }

        public static IEnumerable<FrameTransform> Find(PipelineCore pipeline, Frame frame)
        {
            //avoid a database scan which by definition checks every transform in the database
            IEnumerable<TransformSource> transforms = null;
            lock (frame.Transforms)
            {
                transforms = frame.Transforms.ToArray();
            }
            foreach (var source in transforms)
            {
                yield return Find(pipeline, frame, source);
            }
        }

        public static FrameTransform FindBest(PipelineCore pipeline, Frame frame)
        {
            TransformSource[] transforms = null;
            lock (frame.Transforms)
            {
                transforms = frame.Transforms.ToArray();
            }
            return transforms.Length > 0 ? Find(pipeline, frame, transforms.OrderBy(source => source).First()) : null;
        }

        public static FrameTransform Find(PipelineCore pipeline, string projectName, string frameName,
                                          TransformSource source)
        {
            return pipeline.LoadDatabaseItem<FrameTransform>(MakeName(frameName, source), projectName);
        }

        public static FrameTransform Find(PipelineCore pipeline, Frame frame, TransformSource source)
        {
            return Find(pipeline, frame.ProjectName, frame.Name, source);
        }

        public bool IsPrior()
        {
            return Source >= TransformSource.Prior;
        }

        public static string AppendSourcesPath(string dir, TransformSource[] adjustedSources,
                                               TransformSource[] priorSources, bool usePriors)
        {
            if (usePriors)
            {
                dir += "/prior";
                if (priorSources.Length > 0)
                {
                    dir += "_" + String.Join("_", priorSources);
                }
            }
            else
            {
                dir += "/best";
                if (priorSources.Length > 0)
                {
                    dir += "_" + String.Join("_", priorSources);
                }
                if (adjustedSources.Length > 0)
                {
                    dir += "_" + String.Join("_", adjustedSources);
                }
            }

            return dir;
        }

        public static TransformSource[] ParseSources(string sources)
        {
            return (sources ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => Enum.Parse(typeof(TransformSource), s.Trim(), ignoreCase: true))
                .Cast<TransformSource>()
                .ToArray();
        }
    }
}

