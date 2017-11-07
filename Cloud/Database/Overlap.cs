using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;

namespace OPS.Cloud
{
    //
    [DynamoDBTable("Overlaps")]
    public class Overlap
    {
        //Primary key for dynamoDb. Sorted combination of the two observation names. Cannot be edited directly
        [DynamoDBHashKey]
        [DynamoDBProperty("id")]
        public string Id
        {
            get //construct from an OverlapObs. 
            {
                return Observations.idFromObs;
            }
            set //construct an OverlapObs from this Id. When setter called by Dynamo
            {
                this.Observations = new OverlapObs(value);
            }
        }

        //sort key for dynamoDb in case two projects share observation names 
        [DynamoDBRangeKey]
        [DynamoDBProperty("project_name")]
        public string ProjectName;

        //the observations in this overlap
        [DynamoDBIgnore]
        public OverlapObs Observations;

        //This is set during creation to verify that only one worker can successfully create a single overlap item in Dynamo
        public bool Uploaded { get; set; }

        //S3 URL of image match (for now this is what we're saving)
        //Always upload file before writing MatchUrl to keep state consistent 
        public string MatchUrl { get; set; }

        [DynamoDBVersion]
        public int? VersionNumber { get; set; }

        //S3 location of keypoint map 
        public string keypoints { get; set; }

        //Constructor required by DynamoDb but should not be called otherwise
        public Overlap()
        {

        }

        protected Overlap(string obs1, string obs2, string projectName)
        {
            this.Observations = new OverlapObs(obs1, obs2);
            this.ProjectName = projectName;
        }

        //Public interface for Overlap 

        /// <summary>
        /// Create an overlap between these two observations and save it to the database. 
        /// Returns null if an observation is already in the database. 
        /// Guaranteed that two workers cannot both create the same observation 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="observationName1">Order of observations does not matter</param>
        /// <param name="observationName2"></param>
        /// <returns></returns>
        public static Overlap Create(DynamoDBContext context, string observationName1, string observationName2, string projectName)
        {
            //create an overlap without setting Uploaded
            Overlap newOverlap = new Overlap(observationName1, observationName2, projectName);
            try
            {
                context.Save(newOverlap);
            }
            catch(AmazonDynamoDBException e)
            {
                if (e.ErrorCode == "ConditionalCheckFailedException") return null; //if create fails another worker has already uploaded and updated this overlap
                else throw e; //unexpected error
            }
            

            //set Uploaded=true and save updated Overlap
            newOverlap.Uploaded = true;
            try
            {
                context.Save(newOverlap);
            }
            catch (AmazonDynamoDBException e)
            {
                if (e.ErrorCode == "ConditionalCheckFailedException") return null;
                else throw e;
            }

            //if save was successful, return Overlap with correct version number so it can be saved
            return context.Load<Overlap>(newOverlap.Id, newOverlap.ProjectName, new DynamoDBOperationConfig { ConsistentRead = true});
        }

        public static Overlap Find(DynamoDBContext context, string observationName1, string observationName2, string projectName)
        {
            OverlapObs name = new OverlapObs(observationName1, observationName2);
            return context.Load<Overlap>(name.idFromObs, projectName);
        }
        
        /// <summary>
        /// Helper class to validate an Overlap and convert from the observation names of the 
        /// overlapping observations to the DynamoDB ID for the overlap
        /// </summary>
        public class OverlapObs
        {
            public string obs1 { get; private set; }
            public string obs2 { get; private set; }

            public string idFromObs
            {
                get
                {
                    if (obs1.CompareTo(obs2) < 0) return string.Format("{1}{0}{2}", " x ", obs1, obs2);
                    else return string.Format("{1}{0}{2}", " x ", obs2, obs1);
                }
            }

            /// <summary>
            /// Construct from an id
            /// </summary>
            /// <param name="combinedObs"></param>
            public OverlapObs(string combinedObs)
            {
                string[] names = combinedObs.Split(new string[] { " x " }, StringSplitOptions.None);
                if (names.Count() != 2) throw new CloudException("Could not find observation names in Overlap Id");
                this.obs1 = names[0]; this.obs2 = names[1];
                validate();
            }

            public OverlapObs(string obs1, string obs2)
            {
                this.obs1 = obs1;
                this.obs2 = obs2;
                validate();
            }

            /// <summary>
            /// Only an Overlap with a valid OverlapObs can be saved to the DB
            /// </summary>
            /// <returns></returns>
            public void validate()
            {
                if (!(this.obs1 != null && this.obs2 != null && //an overlap must contain two images
                    !this.obs1.Contains(" x ") && !this.obs2.Contains(" x ") && //names cannot contain the separator used to construct the name
                    this.obs1.CompareTo(this.obs2) != 0)) //an overlap between the same observation is not valid
                    throw new CloudException("Creating an Overlap with invalid observation names. Two observation names must be present and cannot contain ' x '");
            }
        }

    }
}
