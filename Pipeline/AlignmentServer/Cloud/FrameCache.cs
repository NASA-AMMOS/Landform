using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OPS.Pipeline.AlignmentServer
{
    public class FrameCache
    {
        private readonly PipelineCore pipeline;
        private readonly string projectName;

        private readonly Dictionary<string, Frame> frames = new Dictionary<string, Frame>();
        private readonly Dictionary<string, List<Frame>> children = new Dictionary<string, List<Frame>>();
        private readonly Dictionary<string, SortedDictionary<TransformSource, FrameTransform>> transforms =
            new Dictionary<string, SortedDictionary<TransformSource, FrameTransform>>();

        public FrameCache(PipelineCore pipeline, string projectName)
        {
            this.pipeline = pipeline;
            this.projectName = projectName;
        }

        public void Add(Frame frame)
        {
            if (!frames.ContainsKey(frame.Name)) //ensure that children doesn't get duplicates
            {
                frames[frame.Name] = frame;
                if (frame.ParentName != null)
                {
                    if (!children.ContainsKey(frame.ParentName))
                    {
                        children[frame.ParentName] = new List<Frame>();
                    }
                    children[frame.ParentName].Add(frame);
                }
            }
        }

        public void Add(FrameTransform transform)
        {
            if (!transforms.ContainsKey(transform.FrameName))
            {
                transforms[transform.FrameName] = new SortedDictionary<TransformSource, FrameTransform>();
            }
            if (!transforms[transform.FrameName].ContainsKey(transform.Source))
            {
                transforms[transform.FrameName][transform.Source] = transform;
            }
        }

        public int Preload(bool loadTransforms = true)
        {
            Frame.Find(pipeline, projectName).ToList().ForEach(frame => Add(frame));
            foreach (var frame in frames.Keys)
            {
                if (!children.ContainsKey(frame))
                {
                    children[frame] = new List<Frame>(); //leaf node
                }
            }
            if (loadTransforms)
            {
                FrameTransform.Find(pipeline, projectName).ToList().ForEach(transform => Add(transform));
                foreach (var frame in frames.Keys)
                {
                    if (!transforms.ContainsKey(frame))
                    {
                        transforms[frame] = new SortedDictionary<TransformSource, FrameTransform>();
                    }
                }
            }
            return frames.Count;
        }

        public IEnumerable<Frame> GetAllFrames()
        {
            return frames.Values;
        }

        public IEnumerable<FrameTransform> GetAllTransforms()
        {
            foreach (var forFrame in transforms.Values)
            {
                foreach (var transform in forFrame.Values)
                {
                    yield return transform;
                }
            }
        }

        public IEnumerable<Frame> GetChildren(string name)
        {
            if (!children.ContainsKey(name))
            {
                children[name] = new List<Frame>(); //handles case there are none
                GetFrame(name).GetChildren(pipeline).ToList().ForEach(child => Add(child));
            }
            return children[name];
        }

        public IEnumerable<Frame> GetChildren(Frame frame)
        {
            return GetChildren(frame.Name);
        }

        public Frame GetFrame(string name)
        {
            if (!frames.ContainsKey(name))
            {
                var frame = Frame.Find(pipeline, projectName, name);
                if (frame != null)
                {
                    Add(frame);
                }
                else
                {
                    frames[name] = null;
                }
            }
            return frames[name];
        }

        public IEnumerable<FrameTransform> GetTransforms(string name)
        {
            if (!transforms.ContainsKey(name))
            {
                foreach (var transform in FrameTransform.Find(pipeline, GetFrame(name))) Add(transform);
            }
            return transforms[name].Values;
        }

        public IEnumerable<FrameTransform> GetTransforms(Frame frame)
        {
            return GetTransforms(frame.Name);
        }

        public FrameTransform GetBestTransform(string name)
        {
            return GetTransforms(name).FirstOrDefault();
        }

        public FrameTransform GetBestTransform(Frame frame)
        {
            return GetBestTransform(frame.Name);
        }

        public FrameTransform GetBestAdjustedTransform(string name)
        {
            foreach (var transform in GetTransforms(name))
            {
                if (transform.Source < TransformSource.Prior)
                {
                    return transform;
                }
            }
            return null;
        }

        public FrameTransform GetBestAdjustedTransform(Frame frame)
        {
            return GetBestAdjustedTransform(frame.Name);
        }

        public FrameTransform GetBestPrior(string name)
        {
            foreach (var transform in GetTransforms(name))
            {
                if (transform.Source >= TransformSource.Prior)
                {
                    return transform;
                }
            }
            return null;
        }

        public FrameTransform GetBestPrior(Frame frame)
        {
            return GetBestPrior(frame.Name);
        }
    }
}
