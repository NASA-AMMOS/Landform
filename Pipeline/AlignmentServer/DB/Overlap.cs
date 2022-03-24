using System;
using System.Collections.Generic;
using OPS.Cloud;
using System.Linq;

namespace OPS.Pipeline.AlignmentServer
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
    public class Overlap
    {
        [DBRangeKey]
        public string ProjectName;

        [DBHashKey]
        public string CombinedName;

        public string ObservationNameOne;

        public string ObservationNameTwo;

        public enum StatusType
        {
            Proposed = 0,
            Matched,
            Rejected
        }
        public StatusType Status;

        //set during creation to verify that only one worker can successfully create a single overlap item 
        public bool Uploaded;

        //set on computing a match to cache the result; if empty, check status to determine whether to compute
        public Guid MatchGuid;

        public int? VersionNumber;

        public Overlap() { }

        /// <summary>
        /// Constructor for a new overlap
        /// </summary>
        /// <param name="obs1"></param>
        /// <param name="obs2"></param>
        /// <param name="projectName"></param>
        protected Overlap(string projectName, string obs1, string obs2)
        {
            ProjectName = projectName;
            var name = new OverlapName(obs1, obs2);
            ObservationNameOne = name.ObservationNameOne;
            ObservationNameTwo = name.ObservationNameTwo;
            CombinedName = name.CombinedName;
            Status = StatusType.Proposed;
        }

        //Public interface for Overlap 

        /// <summary>
        /// Create an overlap between these two observations and save it to the database. 
        /// Returns null if an observation is already in the database. 
        /// Guaranteed that two workers cannot both create the same overlap 
        /// </summary>
        /// <param name="pipeline"></param>
        /// <param name="observationName1">Order of observations does not matter</param>
        /// <param name="observationName2"></param>
        /// <returns></returns>
        public static Overlap Create(PipelineCore pipeline, string projectName, string obs1, string obs2) 
        {
            //create an overlap without setting Uploaded
            Overlap newOverlap = new Overlap(projectName, obs1, obs2);
            pipeline.SaveDatabaseItem(newOverlap, ignoreErrors: false);

            newOverlap.Uploaded = true;

            pipeline.SaveDatabaseItem(newOverlap, ignoreErrors: false);

            //return Overlap with most recent version number
            return pipeline.LoadDatabaseItem<Overlap>(newOverlap.CombinedName, projectName);
        }

        /// <summary>
        /// Save only if most recent version is being edited. If not, return false 
        /// </summary>
        /// <param name="pipeline"></param>
        public bool TrySave(PipelineCore pipeline)
        {
            pipeline.SaveDatabaseItem(this);
            return true;
        }

        public static Overlap Find(PipelineCore pipeline, string projectName, string obs1, string obs2)
        {
            return Find(pipeline, projectName, new OverlapName(obs1, obs2).CombinedName);
        }

        public static Overlap Find(PipelineCore pipeline, string projectName, string combinedName)
        {
            return pipeline.LoadDatabaseItem<Overlap>(combinedName, projectName);
        }

        public static IEnumerable<Overlap> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<Overlap>("ProjectName", projectName);
        }

        public static IEnumerable<Overlap> FindAllForObservation(PipelineCore pipeline, string projectName,
                                                                 string obsName)
        {
            foreach (var prop in new[] { "ObservationNameOne", "ObservationNameTwo" })
            {
                foreach (var o in pipeline.ScanDatabase<Overlap>("ProjectName", projectName, prop, obsName))
                {
                    yield return o;
                }
            }
        }
    }
}
