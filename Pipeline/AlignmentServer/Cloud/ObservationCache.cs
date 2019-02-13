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
    /// Caches observations keyed on name
    /// </summary>
    public class ObservationCache
    {
        ConcurrentDictionary<string, Observation> observations = new ConcurrentDictionary<string, Observation>();
        ConcurrentDictionary<string, ConcurrentBag<Observation>> obsForFrame =
            new ConcurrentDictionary<string, ConcurrentBag<Observation>>();
        ConcurrentDictionary<string, bool> loadedAllForFrame = new ConcurrentDictionary<string, bool>();

        private readonly PipelineCore pipeline;
        private readonly string projectName;

        public ObservationCache(PipelineCore pipeline, string projectName)
        {
            this.pipeline = pipeline;
            this.projectName = projectName;
        }

        private void AddForFrame(Observation obs)
        {
            obsForFrame.AddOrUpdate(obs.FrameName, _ => {
                    var bag = new ConcurrentBag<Observation>();
                    bag.Add(obs);
                    return bag;
                },
                (_, bag) => {
                    bag.Add(obs);
                    return bag;
                });
        }

        public int Preload()
        {
            int n = 0;
            foreach (var obs in Observation.Find(pipeline, projectName))
            {
                observations.TryAdd(obs.Name, obs);
                AddForFrame(obs);
                loadedAllForFrame.TryAdd(obs.FrameName, true);
                n++;
            }
            return n;
        }

        public IEnumerable<Observation> GetAllObservationsForFrame(Frame frame)
        {
            ConcurrentBag<Observation> bag = null;
            if (loadedAllForFrame.ContainsKey(frame.Name))
            {
                obsForFrame.TryGetValue(frame.Name, out bag);
            }
            else
            {
                bag = obsForFrame.GetOrAdd(frame.Name, _ => new ConcurrentBag<Observation>());
                foreach (var obs in Observation.Find(pipeline, frame))
                {
                    AddForFrame(obs);
                }
                loadedAllForFrame.TryAdd(frame.Name, true);
            }
            return bag;
        }

        public Observation GetObservation(string name)
        {
            var obs = observations.GetOrAdd(name, _ => Observation.Find(pipeline, projectName, name));
            AddForFrame(obs);
            return obs;
        }
    }
}
