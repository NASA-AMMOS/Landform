using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Amazon.DynamoDBv2.DataModel;
using System.Runtime.InteropServices;
using System.Threading;
using OPS.Plumbing;

namespace OPS.Pipeline.AlignmentServer
{
    /// <summary>
    /// queries rover observations one by one with an overly safe throttling (assumes the actual read takes zero time)
    /// </summary>
    public class RoverObservationCache
    {
        Dictionary<string, List<RoverObservation>> obsByFrame = new Dictionary<string, List<RoverObservation>>();
        
        PipelineCore pipeline;
        string projectName;
        int sleepThrottleMS;

        public RoverObservationCache(PipelineCore pipeline, string projectName, int estimatedItemSizeBytes, int tableReadCapacity)
        {
            this.pipeline = pipeline;
            this.projectName = projectName;
            
            sleepThrottleMS = GetThrottleMS(estimatedItemSizeBytes, tableReadCapacity);
        }

        public void FillCache(bool onlyReconstructionObs)
        {
            IEnumerable<RoverObservation> observations = RoverObservation.Find(pipeline, projectName);
            foreach(var observation in observations)
            {
                if(!onlyReconstructionObs || observation.UseForReconstruction)
                {
                    if (!obsByFrame.ContainsKey(observation.FrameName))
                    {
                        obsByFrame.Add(observation.FrameName,new List<RoverObservation>());
                    }

                    obsByFrame[observation.FrameName].Add(observation);
                }
                Thread.Sleep(sleepThrottleMS);
            }
        }

        public List<RoverObservation> GetObsByFrame(string frameName)
        {
            if(!obsByFrame.ContainsKey(frameName))
                return null;

            return new List<RoverObservation>(obsByFrame[frameName]);
        }

        public List<string> GetFrameNamesWithObservations()
        {
            return obsByFrame.Keys.ToList();
        }

        int GetThrottleMS(int estimatedItemSizeBytes, int tableReadCapacity)
        {
            const int dynamoPageSizeBytes = 4000;
            int dynamoBytesPerSec = dynamoPageSizeBytes * tableReadCapacity;
            int obsReadsPerSec = dynamoBytesPerSec / estimatedItemSizeBytes;
            return 1000 / obsReadsPerSec;
        }
    }
}
