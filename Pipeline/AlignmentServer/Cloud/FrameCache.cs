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
        private readonly Dictionary<string, FrameTransform> transforms = new Dictionary<string, FrameTransform>();
        private readonly Dictionary<string, TransformPrior> priors = new Dictionary<string, TransformPrior>();

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
            transforms[transform.FrameName] = transform;
        }

        public void Add(TransformPrior prior)
        {
            priors[prior.FrameName] = prior;
        }
            
        public int Preload(bool loadTransforms = true, bool loadPriors = false)
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
                        transforms[frame] = null;
                    }
                }
            }
            if (loadPriors)
            {
                TransformPrior.Find(pipeline, projectName).ToList().ForEach(prior => Add(prior));
                foreach (var frame in frames.Keys)
                {
                    if (!priors.ContainsKey(frame))
                    {
                        priors[frame] = null;
                    }
                }
            }
            return frames.Count;
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
                frames[name] = null;
                var frame = Frame.Find(pipeline, projectName, name);
                if (frame != null)
                {
                    Add(frame);
                }
            }
            return frames[name];
        }

        public FrameTransform GetTransform(string name)
        {
            if (!transforms.ContainsKey(name))
            {
                transforms[name] = FrameTransform.Find(pipeline, GetFrame(name)); //may be null
            }
            return transforms[name];
        }

        public FrameTransform GetTransform(Frame frame)
        {
            return GetTransform(frame.Name);
        }

        public TransformPrior GetTransformPrior(Frame frame)
        {
            if (!priors.ContainsKey(frame.Name))
            {
                priors[frame.Name] = frame.GetPrior(pipeline); //may be null
            }
            return priors[frame.Name];
        }
    }
}
