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
    /// <summary>
    /// Store the overlap between two observations. 
    /// ID is constructed from the names of the obervations (in sorted order). Any two observations can have at most one overlap. 
    /// Overlaps are versioned, so only one worker can create them and they can only be edited if you have the newest version
    /// Overlaps are versioned because a Match is not deterministic. if a task is started based on a match then another MatchPairs worker overwrites that match, that's an inconsistent state.
    /// </summary>
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
                return Observations.IdFromObs;
            }
            set //construct an OverlapObs from this Id. Used by Dynamo
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
        public Guid MatchGuid { get; set; }

        [DynamoDBVersion]
        public int? VersionNumber { get; set; }

        //Constructor required by DynamoDb but should not be called otherwise
        public Overlap()
        {

        }

        /// <summary>
        /// Constructor for a new overlap
        /// </summary>
        /// <param name="obs1"></param>
        /// <param name="obs2"></param>
        /// <param name="projectName"></param>
        protected Overlap(string obs1, string obs2, string projectName)
        {
            this.Observations = new OverlapObs(obs1, obs2);
            this.ProjectName = projectName;
        }

        //Public interface for Overlap 

        /// <summary>
        /// Create an overlap between these two observations and save it to the database. 
        /// Returns null if an observation is already in the database. 
        /// Guaranteed that two workers cannot both create the same overlap 
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
            catch(ConditionalCheckFailedException)//if create fails another worker has already uploaded and updated this overlap
            {
                return null;
            }

            //set Uploaded=true and save updated Overlap
            newOverlap.Uploaded = true;
            try
            {
                context.Save(newOverlap);
            }
            catch (ConditionalCheckFailedException)//Another worker updated this overlap before we could
            {
                return null;
            }

            //if save was successful, return Overlap with most recent version number so it can be saved
            return context.Load<Overlap>(newOverlap.Id, newOverlap.ProjectName, new DynamoDBOperationConfig { ConsistentRead = true});
        }

        /// <summary>
        /// Save only if most recent version is being edited. If not, return false 
        /// </summary>
        /// <param name="context"></param>
        public bool TrySave(DynamoDBContext context)
        {
            try
            {
                context.Save(this);
            }
            catch (ConditionalCheckFailedException)
            {
                return false;
            }
            return true;
        }

        public static Overlap Find(DynamoDBContext context, string observationName1, string observationName2, string projectName)
        {
            OverlapObs name = new OverlapObs(observationName1, observationName2);
            return context.Load<Overlap>(name.IdFromObs, projectName);
        }
        
        /// <summary>
        /// Helper class to validate an Overlap and convert from the observation names of the 
        /// overlapping observations to the DynamoDB ID for the overlap
        /// </summary>
        public class OverlapObs
        {
            private const string SEPARATOR = " x ";

            public string Obs1 { get; private set; }
            public string Obs2 { get; private set; }

            public string IdFromObs
            {
                get
                {
                    if (Obs1.CompareTo(Obs2) < 0)
                    {
                        return string.Format("{1}{0}{2}", SEPARATOR, Obs1, Obs2);
                    }
                    else
                    {
                        return string.Format("{1}{0}{2}", SEPARATOR, Obs2, Obs1);
                    }
                }
            }

            /// <summary>
            /// Construct from an id
            /// </summary>
            /// <param name="combinedObs"></param>
            public OverlapObs(string combinedObs)
            {
                string[] names = combinedObs.Split(new string[] { SEPARATOR }, StringSplitOptions.None);
                if (names.Count() != 2)
                {
                    throw new CloudException("Could not find observation names in Overlap Id");
                }
                this.Obs1 = names[0];
                this.Obs2 = names[1];
                IsValid();
            }

            public OverlapObs(string obs1, string obs2)
            {
                this.Obs1 = obs1;
                this.Obs2 = obs2;
                IsValid();
            }

            /// <summary>
            /// Only an Overlap with a valid OverlapObs can be saved to the DB
            /// </summary>
            /// <returns></returns>
            public void IsValid()
            {
                if (!(this.Obs1 != null && this.Obs2 != null && //an overlap must contain two images
                    !this.Obs1.Contains(SEPARATOR) && !this.Obs2.Contains(SEPARATOR) && //names cannot contain the separator used to construct the name
                    this.Obs1.CompareTo(this.Obs2) != 0)) //an overlap between the same observation is not valid
                {
                    throw new CloudException("Creating an Overlap with invalid observation names. Two observation names must be present and cannot contain " + SEPARATOR);
                }
            }
        }

    }
}
