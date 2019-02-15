using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OPS.Pipeline.AlignmentServer
{
    public class ObservationCache
    {
        private readonly PipelineCore pipeline;
        private readonly string projectName;

        private readonly Dictionary<string, Observation> observations = new Dictionary<string, Observation>();
        private readonly Dictionary<string, List<Observation>> forFrame = new Dictionary<string, List<Observation>>();

        public ObservationCache(PipelineCore pipeline, string projectName)
        {
            this.pipeline = pipeline;
            this.projectName = projectName;
        }

        public void Add(Observation obs)
        {
            if (!observations.ContainsKey(obs.Name)) //ensure that forFrame doesn't get duplicates
            {
                observations[obs.Name] = obs;
                if (!forFrame.ContainsKey(obs.FrameName))
                {
                    forFrame[obs.FrameName] = new List<Observation>();
                }
                forFrame[obs.FrameName].Add(obs);
            }
        }

        public int Preload(Func<Observation, bool> filter = null)
        {
            RoverObservation.Find(pipeline, projectName).ToList().ForEach(obs => {
                    if (filter == null || filter(obs))
                    {
                        Add(obs);
                    }
                });
            return observations.Count;
        }

        public IEnumerable<Observation> GetAllObservationsForFrame(Frame frame)
        {
            if (!forFrame.ContainsKey(frame.Name))
            {
                RoverObservation.Find(pipeline, frame).ToList().ForEach(obs => Add(obs));
            }
            return forFrame[frame.Name];
        }

        public Observation GetObservation(string name)
        {
            if (!observations.ContainsKey(name))
            {
                Add(RoverObservation.Find(pipeline, projectName, name));
            }
            return observations[name];
        }

        public IEnumerable<string> GetAllFramesWithObservations()
        {
            return forFrame.Keys;
        }
    }
}
