using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;

namespace OPS.Pipeline.AlignmentServer
{
    /// <summary>
    /// Caches overlaps keyed on name
    /// </summary>
    public class OverlapCache
    {
        ConcurrentDictionary<string, Overlap> overlaps = new ConcurrentDictionary<string, Overlap>();
        ConcurrentDictionary<string, ConcurrentBag<Overlap>> overlapsForObservation =
            new ConcurrentDictionary<string, ConcurrentBag<Overlap>>();
        ConcurrentDictionary<string, bool> loadedAllForObservation = new ConcurrentDictionary<string, bool>();

        private readonly PipelineCore pipeline;
        private readonly string projectName;

        public OverlapCache(PipelineCore pipeline, string projectName)
        {
            this.pipeline = pipeline;
            this.projectName = projectName;
        }

        private void AddForObservation(Overlap overlap)
        {
            AddForObservation(overlap, overlap.ObservationNameOne);
            AddForObservation(overlap, overlap.ObservationNameTwo);
        }

        private void AddForObservation(Overlap overlap, string observationName)
        {
            overlapsForObservation.AddOrUpdate(observationName, _ => {
                    var bag = new ConcurrentBag<Overlap>();
                    bag.Add(overlap);
                    return bag;
                },
                (_, bag) => {
                    bag.Add(overlap);
                    return bag;
                });
        }

        public int Preload()
        {
            int n = 0;
            foreach (var overlap in Overlap.Find(pipeline, projectName))
            {
                overlaps.TryAdd(overlap.CombinedName, overlap);
                AddForObservation(overlap);
                loadedAllForObservation.TryAdd(overlap.ObservationNameOne, true);
                loadedAllForObservation.TryAdd(overlap.ObservationNameTwo, true);
                n++;
            }
            return n;
        }

        public IEnumerable<Overlap> GetAllOverlapsForObservation(Observation observation)
        {
            ConcurrentBag<Overlap> bag = null;
            if (loadedAllForObservation.ContainsKey(observation.Name))
            {
                overlapsForObservation.TryGetValue(observation.Name, out bag);
            }
            else
            {
                bag = overlapsForObservation.GetOrAdd(observation.Name, _ => new ConcurrentBag<Overlap>());
                foreach (var overlap in Overlap.Find(pipeline, observation))
                {
                    AddForObservation(overlap);
                }
                loadedAllForObservation.TryAdd(observation.Name, true);
            }
            return bag;
        }

        public Overlap GetOverlap(string name)
        {
            return overlaps.GetOrAdd(name, _ => Overlap.Find(pipeline, projectName, name));
        }
    }
}
