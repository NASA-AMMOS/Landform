using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Xna.Framework;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.Infrastructure;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;

namespace OPS.Cloud
{
    public static class TransformSource
    {
        public const string Prior = "prior";
        public const string Derived = "derived";
    }



    /// <summary>
    /// Represents the rotation and translation between two frames
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
        public double Error { get; set; }

        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double QX { get; set; }
        public double QY { get; set; }
        public double QZ { get; set; }
        public double QW { get; set; }

        [DynamoDBVersion]
        public int? VersionNumber { get; set; }

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
            this.Error = error;
        }

        /// <summary>
        /// Creates a transform between two frames and saves it to the database
        /// Returns the transform with a valid id
        /// Returns null if the transform could not be created
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
            catch (AmazonDynamoDBException e) //probably just that the frame already exists
            {
                if (e.ErrorCode != "ConditionalCheckFailedException") throw e;
            }

            //attempt to upload to the lookup table. Backoff and repeat if high traffic to this from/to pair. 
            //TODO check how often this is happening. May need a workaround. 
            Random rand = new Random();
            for (int i = 0; i < 4; i++)
            {
                //Get the most recent lookup record 
                FrameTransformLookup lookup = context.Load<FrameTransformLookup>(fromFrame.Name, toFrame.Name, new DynamoDBOperationConfig{ConsistentRead=true});
                if (lookup.Ids == null) lookup.Ids = new Dictionary<string, HashSet<string>>();
                if (!lookup.Ids.ContainsKey(fromFrame.ProjectName)) lookup.Ids[fromFrame.ProjectName] = new HashSet<string>();
                lookup.Ids[fromFrame.ProjectName].Add(id);
                try
                {
                    context.Save(lookup);
                    break;
                }
                catch (AmazonDynamoDBException e)
                {
                    if (e.ErrorCode == "ConditionalCheckFailedException" && i < 3)
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
            //get list of IDs from lookup table 
            FrameTransformLookup lookup = context.Load<FrameTransformLookup>(fromFrame.Name, toFrame.Name);
            if (lookup == null || lookup.Ids == null ||
                !lookup.Ids.ContainsKey(fromFrame.ProjectName) ||
                lookup.Ids[fromFrame.ProjectName].Count == 0)
            {
                return new HashSet<FrameTransform>(); //none saved 
            }
            //lookup ids in FrameTransform table 
            HashSet<FrameTransform> transforms = new HashSet<FrameTransform>();
            foreach (string id in lookup.Ids[fromFrame.ProjectName])
            {
                FrameTransform ft = context.Load<FrameTransform>(id, fromFrame.ProjectName);
                if (ft != null) transforms.Add(ft);
            }
            return transforms;
        }


        [DynamoDBIgnore]
        public Vector3 Translation
        {
            get
            {
                return new Vector3(X, Y, Z);
            }
            set
            {
                this.X = value.X;
                this.Y = value.Y;
                this.Z = value.Z;
            }
        }

        [DynamoDBIgnore]
        public Quaternion Rotation
        {
            get
            {
                return new Quaternion(QX, QY, QZ, QW);
            }
            set
            {
                this.QX = value.X;
                this.QY = value.Y;
                this.QZ = value.Z;
                this.QW = value.W;
            }
        }


        /// <summary>
        /// Record the ID for a frame transform 
        /// If a transform is in the database, it will always be in the Lookup table. 
        /// However, a transform in the lookup table may not always be in the Transform table 
        /// Should not be used except by FrameTransform class 
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
            public FrameTransformLookup(string FromFrameName, string ToFrameName)
            {
                this.FromFrameName = FromFrameName;
                this.ToFrameName = ToFrameName;
                Ids = new Dictionary<string, HashSet<string>>();
            }
        }
    }
}
