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
    class OverlapName
    {
        public string ObservationNameOne;
        public string ObservationNameTwo;
        public string CombinedName;

        public OverlapName(string obs1, string obs2)
        {
            if (obs1.CompareTo(obs2) <= 0)
            {
                ObservationNameOne = obs1;
                ObservationNameTwo = obs2;
            }
            else
            {
                ObservationNameOne = obs2;
                ObservationNameTwo = obs1;
            }
            CombinedName = obs1 + "~X~" + obs2;
        }
    }

    /// <summary>
    /// Store the overlap between two observations. 
    /// ID is constructed from the names of the obervations (in sorted order). Any two observations can have at most one overlap. 
    /// Overlaps are versioned, so only one worker can create them and they can only be edited if you have the newest version
    /// Overlaps are versioned because a Match is not deterministic. if a task is started based on a match then another MatchPairs worker overwrites that match, that's an inconsistent state.
    /// </summary>
    [DynamoDBTable("Overlaps")]
    public class Overlap
    {
        [DynamoDBRangeKey]
        [DynamoDBGlobalSecondaryIndexRangeKey("OverlapObservationOneIndex", "OverlapObservationTwoIndex")]
        [DynamoDBProperty]
        public string ProjectName;

        [DynamoDBHashKey]
        [DynamoDBProperty]
        public string CombinedName;

        [DynamoDBGlobalSecondaryIndexHashKey("OverlapObservationOneIndex")]
        [DynamoDBProperty]
        public string ObservationNameOne;

        [DynamoDBGlobalSecondaryIndexHashKey("OverlapObservationTwoIndex")]
        [DynamoDBProperty]
        public string ObservationNameTwo;

        public enum StatusType
        {
            Proposed = 0,
            Matched,
            Rejected
        }
        [DynamoDBProperty]
        public StatusType Status;

        //This is set during creation to verify that only one worker can successfully create a single overlap item in Dynamo
        public bool Uploaded { get; set; }
        [DynamoDBProperty]
        public Guid MatchGuid { get; set; }

        [DynamoDBVersion]
        public int? VersionNumber { get; set; }

        //Constructor required by DynamoDb but should not be called otherwise
        public Overlap() { }

        /// <summary>
        /// Constructor for a new overlap
        /// </summary>
        /// <param name="obs1"></param>
        /// <param name="obs2"></param>
        /// <param name="projectName"></param>
        protected Overlap(string obs1, string obs2, string projectName)
        {
            var name = new OverlapName(obs1, obs2);
            ObservationNameOne = name.ObservationNameOne;
            ObservationNameTwo = name.ObservationNameTwo;
            CombinedName = name.CombinedName;
            ProjectName = projectName;
            Status = StatusType.Proposed;
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
        public static Overlap Create(DynamoDBContext context, Observation observation1, Observation observation2)
        {
            //create an overlap without setting Uploaded
            Overlap newOverlap = new Overlap(observation1.Name, observation2.Name, observation1.ProjectName);
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
            return context.Load<Overlap>(newOverlap.CombinedName, newOverlap.ProjectName, new DynamoDBOperationConfig { ConsistentRead = true});
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
            var name = new OverlapName(observationName1, observationName2);
            return context.Load<Overlap>(name.CombinedName, projectName);
        }

        public static IEnumerable<Overlap> Find(DynamoDBContext context, Observation observation)
        {
            foreach (var prop in new[] { "ObservationOneName", "ObservationTwoName" })
            {
                var filt = new Amazon.DynamoDBv2.DocumentModel.QueryFilter(
                        prop, Amazon.DynamoDBv2.DocumentModel.QueryOperator.Equal, observation.Name
                        );
                filt.AddCondition("ProjectName", Amazon.DynamoDBv2.DocumentModel.QueryOperator.Equal, observation.ProjectName);
                var entries = context.FromQuery<Overlap>(new Amazon.DynamoDBv2.DocumentModel.QueryOperationConfig()
                {
                    IndexName = prop + "Index",
                    Filter = filt
                });
                foreach (var o in entries)
                {
                    yield return o;
                }
            }
        }
    }
}
