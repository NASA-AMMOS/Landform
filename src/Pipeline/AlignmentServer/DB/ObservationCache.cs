using System;
using System.Collections.Generic;
using System.Linq;

namespace JPLOPS.Pipeline.AlignmentServer
{
    public class ObservationCache
    {
        private readonly PipelineCore pipeline;
        private readonly string projectName;

        //Observation name -> Observation
        private readonly Dictionary<string, Observation> observations = new Dictionary<string, Observation>();

        //Observation index -> Observation
        private readonly Dictionary<int, Observation> indexedObservations = new Dictionary<int, Observation>();

        //Frame name -> Observations
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
                indexedObservations[obs.Index] = obs;
                if (!forFrame.ContainsKey(obs.FrameName))
                {
                    forFrame[obs.FrameName] = new List<Observation>();
                }
                forFrame[obs.FrameName].Add(obs);
            }
        }

        public bool Remove(Observation obs)
        {
            if (!observations.ContainsKey(obs.Name))
            {
                return false;
            }
            observations.Remove(obs.Name);
            if (forFrame.ContainsKey(obs.FrameName))
            {
                forFrame[obs.FrameName].Remove(obs);
            }
            indexedObservations.Remove(obs.Index);
            return true;
        }

        public int Preload(Func<Observation, bool> filter = null)
        {
            void maybeAddObs(Observation obs)
            {
                if (filter == null || filter(obs))
                {
                    Add(obs);
                }
            }

            RoverObservation.Find(pipeline, projectName).ToList().ForEach(maybeAddObs); //surface observations

            Observation.Find(pipeline, projectName).ToList().ForEach(maybeAddObs); //other observations incl orbital

            foreach (var obs in observations.Values)
            {
                if (!forFrame.ContainsKey(obs.FrameName))
                {
                    forFrame[obs.FrameName] = new List<Observation>(); //frame has no observations
                }
            }
            return observations.Count;
        }

        public int NumObservations()
        {
            return observations.Count;
        }

        public IEnumerable<Observation> GetAllObservations()
        {
            return observations.Values;
        }

        public IEnumerable<Observation> GetAllObservationsForFrame(Frame frame)
        {
            if (!forFrame.ContainsKey(frame.Name))
            {
                forFrame[frame.Name] = new List<Observation>(); //handles case there are none
                RoverObservation.Find(pipeline, frame).ToList().ForEach(Add); //surface observations
                Observation.Find(pipeline, frame).ToList().ForEach(Add); //other observations incl orbital
            }
            return forFrame[frame.Name];
        }

        public Observation GetObservation(string name)
        {
            if (!observations.ContainsKey(name))
            {
                var obs = RoverObservation.Find(pipeline, projectName, name);
                if (obs != null)
                {
                    Add(obs);
                }
                else
                {
                    observations[name] = null;
                }
            }
            return observations[name];
        }

        public bool ContainsObservation(string name)
        {
            return observations.ContainsKey(name);
        }

        /// <summary>
        /// Unlike GetObservation(string) this only works if the observation has already been loaded into the cache.
        /// </summary>
        public Observation GetObservation(int index)
        {
            if (!indexedObservations.ContainsKey(index))
            {
                return null;
            }
            return indexedObservations[index];
        }

        public bool ContainsObservation(int index)
        {
            return indexedObservations.ContainsKey(index);
        }

        public IEnumerable<string> GetAllFramesWithObservations()
        {
            foreach (var pair in forFrame)
            {
                if (pair.Value.Count > 0)
                {
                    yield return pair.Key;
                }
            }
        }

        public Observation[] ParseList(string list)
        {
            return (list ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => GetObservation(s.Trim()))
                .Where(o => o != null)
                .ToArray();
        }
    }
}
