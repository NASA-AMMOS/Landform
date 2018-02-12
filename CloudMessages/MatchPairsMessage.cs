using Amazon.SQS.Model;

namespace OPS.Cloud
{
    /// <summary>
    /// Alignment pipeline message
    /// Sent by alignment workers when the FindOverlaps task finds two potentially overlapping messages 
    /// </summary>
    [Message("MATCH_PAIRS")]
    public class MatchPairsMessage : PipelineMessage
    {
        [Field("ObservationName")]
        public string ObservationName0 { get; set; }

        [Field("ObservationName2")]
        public string ObservationName1 { get; set; }

        [Field("ProjectName")]
        public string ProjectName { get; set; }

        public MatchPairsMessage() { }
        public MatchPairsMessage(string observationName, string observationName2, string projectName)
        {
            this.ObservationName0 = observationName;
            this.ObservationName1 = observationName2;
            this.ProjectName = projectName;
        }
    }
}
