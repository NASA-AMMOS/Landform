using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Amazon.DynamoDBv2.DataModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace OPS.Pipeline.AlignmentServer
{
    public class RoverObservationCache
    {
        private Dictionary<string, List<RoverObservation>> obsByFrame =
            new Dictionary<string, List<RoverObservation>>();
        
        private readonly PipelineCore pipeline;
        private readonly string projectName;

        public RoverObservationCache(PipelineCore pipeline, string projectName)
        {
            this.pipeline = pipeline;
            this.projectName = projectName;
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
    }
}
