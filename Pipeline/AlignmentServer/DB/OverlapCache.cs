using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OPS.Pipeline.AlignmentServer
{
    public class OverlapCache
    {
        private readonly PipelineCore pipeline;
        private readonly string projectName;

        private readonly Dictionary<string, Overlap> overlaps = new Dictionary<string, Overlap>();
        private readonly Dictionary<string, List<Overlap>> forObs = new Dictionary<string, List<Overlap>>();

        public OverlapCache(PipelineCore pipeline, string projectName)
        {
            this.pipeline = pipeline;
            this.projectName = projectName;
        }

        public void Add(Overlap overlap)
        {
            if (!overlaps.ContainsKey(overlap.CombinedName)) //ensure that forObs doesn't get duplicates
            {
                overlaps[overlap.CombinedName] = overlap;
                AddForObs(overlap, overlap.ObservationNameOne);
                AddForObs(overlap, overlap.ObservationNameTwo);
            }
        }

        private void AddForObs(Overlap overlap, string observationName)
        {
            if (!forObs.ContainsKey(observationName))
            {
                forObs[observationName] = new List<Overlap>();
            }
            forObs[observationName].Add(overlap);
        }

        public int Preload()
        {
            Overlap.Find(pipeline, projectName).ToList().ForEach(overlap => Add(overlap));
            foreach (var obs in overlaps.Keys)
            {
                if (!forObs.ContainsKey(obs))
                {
                    forObs[obs]  = new List<Overlap>(); //no overlaps for this observation
                }
            }
            return overlaps.Count;
        }

        public int NumOverlaps()
        {
            return overlaps.Count;
        }

        public IEnumerable<Overlap> GetAllOverlaps()
        {
            return overlaps.Values;
        }

        public IEnumerable<Overlap> GetAllOverlapsForObservation(Observation observation)
        {
            if (!forObs.ContainsKey(observation.Name))
            {
                forObs[observation.Name] = new List<Overlap>(); //handles case there are none
                Overlap.FindAllForObservation(pipeline, observation.ProjectName, observation.Name)
                    .ToList()
                    .ForEach(o => Add(o));
            }
            return forObs[observation.Name];
        }

        public Overlap GetOverlap(string name)
        {
            if (!overlaps.ContainsKey(name))
            {
                var overlap = Overlap.Find(pipeline, projectName, name);
                if (overlap != null)
                {
                    Add(overlap);
                }
                else
                {
                    overlaps[name] = null;
                }
            }
            return overlaps[name];
        }

        public bool ContainsOverlap(string name)
        {
            return overlaps.ContainsKey(name);
        }
    }
}
