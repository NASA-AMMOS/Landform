using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Xna.Framework;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2;
using MathNet.Numerics.LinearAlgebra;

namespace OPS.Cloud
{
    public static class TransformSource
    {
        public const string Prior = "prior";
        public const string Derived = "derived";
    }

    /// <summary>
    /// Represents the rotation and translation between two frames
    /// Frame transforms are not versioned, so two workers can edit and save them at the same time. 
    /// Frame transform lookups are versioned, but this is internal to the class and workers do not need to worry about it
    /// </summary>
    [DynamoDBTable("FrameTransforms")]
    public class FrameTransform
    {
        [DynamoDBHashKey]
        [DynamoDBProperty("id")]
        public string Id { get; set; }

        [DynamoDBRangeKey]
        [DynamoDBProperty("project_name")]
        public string ProjectName { get; set; }

        public string FromFrameName { get; set; }
        public string ToFrameName { get; set; }
        public string TransformSource { get; set; }

        [DynamoDBProperty("mean", typeof(VectorNConverter))]
        public Vector<double> Mean { get; set; }
        [DynamoDBProperty("covariance", typeof(SquareMatrixConverter))]
        public Matrix<double> Covariance { get; set; }
        

        //This constructor must be public for DynamoDb but should not be used
        public FrameTransform()
        {
            
        }

        /// <summary>
        /// Creates a new transform specifying the relationship between two frames
        /// </summary>
        /// <param name="fromFrame"></param>
        /// <param name="toFrame"></param>
        /// <param name="translation"></param>
        /// <param name="rotation"></param>
        /// <param name="transformSource"></param>
        /// <param name="error"></param>
        protected FrameTransform(string id, Frame fromFrame, Frame toFrame, Vector3 translation, Quaternion rotation, string transformSource, double error)
        {
            this.Id = id;
            this.ProjectName = fromFrame.ProjectName;
            this.FromFrameName = fromFrame.Name;
            this.ToFrameName = toFrame.Name;
            this.Translation = translation;
            this.Rotation = rotation;
            this.TransformSource = transformSource;
        }

        /// <summary>
        /// Creates a transform between two frames and saves it to the database
        /// Saves the lookup for that transform in a lookup entry 
        /// Returns null if the transform already exists or could not be created
        /// </summary>
        /// <param name="context"></param>
        /// <param name="fromFrame"></param>
        /// <param name="toFrame"></param>
        /// <param name="translation"></param>
        /// <param name="rotation"></param>
        /// <param name="transformSource"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public static FrameTransform Create(DynamoDBContext context, Frame fromFrame, Frame toFrame, Vector3 translation, Quaternion rotation, string transformSource, double error)
        {
            //generate a guid for this transform 
            string id = Guid.NewGuid().ToString();

            //Create empty lookup if it does not exist. Must be created empty since version number check doesn't catch creation overwrites 
            try
            {
                context.Save<FrameTransformLookup>(new FrameTransformLookup(fromFrame.Name, toFrame.Name));
            }
            catch (ConditionalCheckFailedException) { } 

            //attempt to upload to the lookup table. Backoff and repeat if high traffic to this from/to pair. 
            //TODO check how often this is happening. May need a workaround. 
            //  We could just use the low-level API and create an add query for the set
            Random rand = new Random();
            for (int i = 0; i < 4; i++)
            {
                //Get the most recent lookup record 
                FrameTransformLookup lookup = context.Load<FrameTransformLookup>(fromFrame.Name, toFrame.Name, new DynamoDBOperationConfig{ConsistentRead=true});
                if (lookup.Ids == null)
                {
                    lookup.Ids = new Dictionary<string, HashSet<string>>();
                }
                if (!lookup.Ids.ContainsKey(fromFrame.ProjectName))
                {
                    lookup.Ids[fromFrame.ProjectName] = new HashSet<string>();
                }
                lookup.Ids[fromFrame.ProjectName].Add(id);
                try
                {
                    context.Save(lookup);
                    break;
                }
                catch (ConditionalCheckFailedException)
                {
                    if (i < 3)
                    {
                        Thread.Sleep(rand.Next(1, 500));
                    }
                    else return null; //create failed
                }
            }

            //Now that the id has been saved to the lookup table, we are free to add the transform itself 
            FrameTransform ft = new FrameTransform(id, fromFrame, toFrame, translation, rotation, transformSource, error);
            context.Save(ft);

            return ft;
        }


        /// <summary>
        /// Find all FrameTransforms that map fromFrame->toFrame
        /// Returns an empty set if none found
        /// </summary>
        /// <param name="context"></param>
        /// <param name="fromFrame"></param>
        /// <param name="toFrame"></param>
        /// <returns></returns>
        public static IEnumerable<FrameTransform> Find(DynamoDBContext context, Frame fromFrame, Frame toFrame)
        {
            return Find(context, fromFrame.Name, toFrame.Name, fromFrame.ProjectName);
        }

        /// <summary>
        /// In most cases we'll know the frame names and it's a waste of Dynamo read capacity units to pull them out just to find the transform.
        /// TODO why do we need the Frames table? 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="fromFrameName"></param>
        /// <param name="toFrameName"></param>
        /// <param name="projectName"></param>
        /// <returns></returns>
        public static IEnumerable<FrameTransform> Find(DynamoDBContext context, string fromFrameName, string toFrameName, string projectName)
        {
            //get list of IDs from lookup table 
            FrameTransformLookup lookup = context.Load<FrameTransformLookup>(fromFrameName, toFrameName);
            if (lookup == null || lookup.Ids == null ||
                !lookup.Ids.ContainsKey(projectName) ||
                lookup.Ids[projectName].Count == 0)
            {
                return new HashSet<FrameTransform>(); //none saved 
            }
            //lookup ids in FrameTransform table 
            HashSet<FrameTransform> transforms = new HashSet<FrameTransform>();
            foreach (string id in lookup.Ids[projectName])
            {
                FrameTransform ft = context.Load<FrameTransform>(id, projectName);
                if (ft != null) transforms.Add(ft);
            }
            return transforms;
        }

        
        /// <summary>
        /// Lookup the IDs of frame transforms between any two frames 
        /// 
        /// Table structure: 
        ///     There could be many transforms mapping between any two frames. Each has a unique GUID. 
        ///     This table allows lookups of all transforms given two frame names and the project name. 
        ///     The table keys are the frame names, so lookups either for "all frame transforms from this frame" or "all frame transforms between these two frames" are fast. 
        ///     The table has an index with the reverse key order, for searches like "all frame transforms to this frame" 
        /// 
        /// Data guarantees: 
        ///     If a transform is in the database, it will always be in the Lookup table. 
        ///     However, a transform in the lookup table may not always be in the Transform table 
        ///     Should not be used except by FrameTransform class 
        /// </summary>
        [DynamoDBTable("FrameTransformLookup")]
        protected class FrameTransformLookup
        {
            [DynamoDBHashKey]
            [DynamoDBProperty("from_frame_name")]
            public string FromFrameName { get; set; }

            [DynamoDBRangeKey]
            [DynamoDBProperty("to_frame_name")]
            public string ToFrameName { get; set; }

            public Dictionary<string, HashSet<String>> Ids { get; set; }

            [DynamoDBVersion]
            public int? VersionNumber { get; set; }

            public FrameTransformLookup()
            {

            }

            //Should always be constructed empty, since simultaneous creates to DynamoDB can overwrite each other. 
            //After an empty entry has been saved, updates will be protected by version number checks
            public FrameTransformLookup(string FromFrameName, string ToFrameName)
            {
                this.FromFrameName = FromFrameName;
                this.ToFrameName = ToFrameName;
                Ids = new Dictionary<string, HashSet<string>>();
            }
        }
    }
}
