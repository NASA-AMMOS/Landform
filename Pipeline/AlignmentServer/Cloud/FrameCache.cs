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
    /// Caches frames and transforms from database keyed on name
    /// </summary>
    public class FrameCache
    {
        ConcurrentDictionary<string, Frame> frames = new ConcurrentDictionary<string, Frame>();
        ConcurrentDictionary<string, FrameTransform> transforms = new ConcurrentDictionary<string, FrameTransform>();
        ConcurrentDictionary<string, ConcurrentBag<Frame>> children =
            new ConcurrentDictionary<string, ConcurrentBag<Frame>>();
        ConcurrentDictionary<string, bool> loadedChildren = new ConcurrentDictionary<string, bool>();

        private readonly PipelineCore pipeline;
        private readonly string projectName;

        public FrameCache(PipelineCore pipeline, string projectName)
        {
            this.pipeline = pipeline;
            this.projectName = projectName;
        }

        private void AddChild(Frame frame)
        {
            var parentName = frame.ParentName;

            if (parentName == null)
            {
                parentName = ""; //parent of root
            }

            children.AddOrUpdate(parentName, _ => {
                    var bag = new ConcurrentBag<Frame>();
                    bag.Add(frame);
                    return bag;
                },
                (_, bag) => {
                    bag.Add(frame);
                    return bag;
                });
        }

        public int Preload()
        {
            int n = 0;
            HashSet<string> all = new HashSet<string>();
            HashSet<string> parents = new HashSet<string>();
            foreach (var frame in Frame.Find(pipeline, projectName))
            {
                all.Add(frame.Name);
                parents.Add(frame.ParentName);
                frames.TryAdd(frame.Name, frame);
                AddChild(frame);
                n++;
            }
            foreach (var frame in all)
            {
                if (!parents.Contains(frame))
                {
                    //add empty bag of children for leaves
                    children.TryAdd(frame, new ConcurrentBag<Frame>());
                }
                loadedChildren.TryAdd(frame, true);
            }
            return n;
        }

        public IEnumerable<Frame> GetChildren(string name)
        {
            ConcurrentBag<Frame> bag = null;
            if (loadedChildren.ContainsKey(name))
            {
                children.TryGetValue(name, out bag);
            }
            else
            {
                bag = children.GetOrAdd(name, _ => new ConcurrentBag<Frame>());
                foreach (var child in GetFrame(name).GetChildren(pipeline))
                {
                    AddChild(child);
                }
                loadedChildren.TryAdd(name, true);
            }
            return bag;
        }

        public IEnumerable<Frame> GetChildren(Frame frame)
        {
            return GetChildren(frame.Name);
        }

        public Frame GetFrame(string name)
        {
            var frame = frames.GetOrAdd(name, _ => Frame.Find(pipeline, projectName, name));
            AddChild(frame);
            return frame;
        }

        public FrameTransform GetTransform(string name)
        {
            return transforms.GetOrAdd(name, _ => FrameTransform.Find(pipeline, GetFrame(name)));
        }

        public FrameTransform GetTransform(Frame frame)
        {
            return GetTransform(frame.Name);
        }
    }
}
