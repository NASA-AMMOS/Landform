using Amazon.SQS.Model;

namespace OPS.Cloud
{
    /// <summary>
    /// Alignment pipeline message. 
    /// Sent by workers when they are done finding features for an image. 
    /// Starts a findOverlaps task, which scans other observations for potential overlaps. 
    /// </summary>
    /// <remarks>
    /// Needs an additional delay to avoid the need for a strongly consistent read in the FindOverlaps task. 
    /// If there was no delay, two FindOverlaps tasks for overlapping observation occuring simultaneously right after the upload of those observations
    /// could both miss observing the other observation, and no overlap would be recorded for those two observations. 
    /// </remarks>
    [Message("FIND_OVERLAPS", delaySeconds: 60)]
    public class FindOverlapsMessage : PipelineMessage
    {
        [Field("ObservationName")]
        public string ObservationName { get; set; }

        public FindOverlapsMessage() { }
        public FindOverlapsMessage(string observationName)
        {
            this.ObservationName = observationName;
        }
    }
}
