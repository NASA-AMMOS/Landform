using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OPS.Geometry;

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


        /// <summary>
        /// convenience function for the common case of allowing all frames but filtering transforms based on parameters
        /// </summary>
        public int PreloadFilteredTransforms(TransformSource[] priorSources, TransformSource[] adjustedSources, bool usePriors)
        {
            Func<FrameTransform, bool> filterPrior =
                   transform => priorSources.Length == 0 || priorSources.Any(s => s == transform.Source);
            Func<FrameTransform, bool> filterAdjusted =
                transform => adjustedSources.Length == 0 || adjustedSources.Any(s => s == transform.Source);

            return Preload(loadTransforms: true, transformFilter: ft =>
                              (!usePriors || ft.IsPrior()) &&      //iff --usepriors only allow priors
                              ((ft.IsPrior() && filterPrior(ft)) ||        //iff --priorsources only allow specific priors
                              (!ft.IsPrior() && filterAdjusted(ft))));    //iff --adjustedsources only allow specific adj
        }

        public int Preload(bool loadTransforms = true, Func<Frame, bool> frameFilter = null,
                           Func<FrameTransform, bool> transformFilter =  null)
        {
            Frame.Find(pipeline, projectName).ToList().ForEach(frame => {
                    if (frameFilter == null || frameFilter(frame))
                    {
                        Add(frame);
                    }
                });
            foreach (var frame in frames.Keys)
            {
                if (!children.ContainsKey(frame))
                {
                    children[frame] = new List<Frame>(); //leaf node
                }
            }
            if (loadTransforms)
            {
                FrameTransform.Find(pipeline, projectName).ToList().ForEach(transform => {
                        if (transformFilter == null || transformFilter(transform))
                        {
                            Add(transform);
                        }
                    });
                if (pipeline.LegacyCompat)
                {
                    foreach (var ft in pipeline.ScanDatabase<FrameTransform>(null, tableName: "FrameTransformPriors"))
                    {
                        ft.Source = TransformSource.Prior;
                        Add(ft);
                    }
                    if (frames.ContainsKey("root"))
                    {
                        //root frame doesn't have a prior in the legacy database, but it's just identity
                        Add(new FrameTransform(frames["root"], TransformSource.Prior, new UncertainRigidTransform()));
                    }
                }
                foreach (var frame in frames.Keys)
                {
                    if (!transforms.ContainsKey(frame))
                    {
                        pipeline.LogWarn("frame \"{0}\" has no transforms!", frame);
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
            var adjustedTransforms = GetTransforms(name).Where(t => t.Source < TransformSource.Prior);
            if (adjustedTransforms == null || adjustedTransforms.Count() == 0)
                return null;

            //transforms are in a sorted dictionary, where lower source number is higher priority
            return adjustedTransforms.First();
        }

        public FrameTransform GetBestAdjustedTransform(Frame frame)
        {
            return GetBestAdjustedTransform(frame.Name);
        }

        public FrameTransform GetBestPrior(string name)
        {
            var priorTransforms = GetTransforms(name).Where(t => t.Source >= TransformSource.Prior);
            if (priorTransforms == null || priorTransforms.Count() == 0)
                return null;

            //transforms are in a sorted dictionary, where lower source number is higher priority
            return priorTransforms.First(); 
        }

        public FrameTransform GetBestPrior(Frame frame)
        {
            return GetBestPrior(frame.Name);
        }
    }
}
