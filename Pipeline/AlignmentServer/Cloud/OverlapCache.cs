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
        private readonly Dictionary<string, List<Overlap>> forObservation = new Dictionary<string, List<Overlap>>();

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
            if (!forObservation.ContainsKey(observationName))
            {
                forObservation[observationName] = new List<Overlap>();
            }
            forObservation[observationName].Add(overlap);
        }

        public int Preload()
        {
            Overlap.Find(pipeline, projectName).ToList().ForEach(overlap => Add(overlap));
            return overlaps.Count;
        }

        public IEnumerable<Overlap> GetAllOverlapsForObservation(Observation observation)
        {
            if (!forObservation.ContainsKey(observation.Name))
            {
                Overlap.FindAllForObservation(pipeline, observation.ProjectName, observation.Name)
                    .ToList()
                    .ForEach(overlap => Add(overlap));
            }
            return forObservation[observation.Name];
        }

        public Overlap GetOverlap(string name)
        {
            if (!overlaps.ContainsKey(name))
            {
                Add(Overlap.Find(pipeline, projectName, name));
            }
            return overlaps[name];
        }
    }
}
