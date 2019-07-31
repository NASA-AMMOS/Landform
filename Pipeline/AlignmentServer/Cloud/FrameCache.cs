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
        public int PreloadFilteredTransforms(TransformSource[] priorSources, TransformSource[] adjustedSources,
                                             bool usePriors = false, bool noPriors = false)
        {
            Func<FrameTransform, bool> filterPrior =
                   transform => priorSources.Length == 0 || priorSources.Any(s => s == transform.Source);
            Func<FrameTransform, bool> filterAdjusted =
                transform => adjustedSources.Length == 0 || adjustedSources.Any(s => s == transform.Source);

            return Preload(loadTransforms: true, transformFilter: ft =>
                           (!usePriors || ft.IsPrior()) &&          //iff --usepriors only allow priors
                           (!noPriors || !ft.IsPrior()) &&          //iff --nopriors only allow adjusted
                           ((ft.IsPrior() && filterPrior(ft)) ||    //iff --priorsources only allow specific priors
                            (!ft.IsPrior() && filterAdjusted(ft))));//iff --adjustedsources only allow specific adj
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

        public bool ContainsFrame(string name)
        {
            return frames.ContainsKey(name);
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

        public bool HasAnyTransform(Frame frame)
        {
            return HasAnyTransform(frame.Name);
        }

        public bool HasAnyTransform(string name)
        {
            return GetTransforms(name).Count() > 0;
        }

        public bool HasPriorTransform(Frame frame)
        {
            return HasPriorTransform(frame.Name);
        }

        public bool HasPriorTransform(string name)
        {
            return GetTransforms(name).Where(t => t.Source >= TransformSource.Prior).Count() > 0;
        }

        public bool HasAdjustedTransform(Frame frame)
        {
            return HasAdjustedTransform(frame.Name);
        }

        public bool HasAdjustedTransform(string name)
        {
            return GetTransforms(name).Where(t => t.Source < TransformSource.Prior).Count() > 0;
        }

        /// <summary>
        /// get transform from an observation frame to the corresponding rover, sitedrive, or root frame
        /// also works to get a transform from an observationframe to any other observation frame
        /// result is null if the transform could not be resolved
        /// if usePriors = true then only prior transform sources will be used
        /// if onlyAligned = true then the result will be null unless at least one transform in the chain is not a prior
        /// </summary>
        public UncertainRigidTransform GetObservationTransform(Observation fromObs, string toFrame,
                                                               bool usePriors = false, bool onlyAligned = false)
        {
            if (toFrame == "rover")
            {
                return new UncertainRigidTransform(); //identity, no uncertainty
            }

            Frame fromFrame = GetFrame(fromObs.FrameName);
            if (fromFrame == null)
            {
                return null;
            }

            if (toFrame == "sitedrive")
            {
                var obsToSD = usePriors ? GetBestPrior(fromFrame) : GetBestTransform(fromFrame);
                return (obsToSD == null || (onlyAligned && obsToSD.IsPrior())) ? null : obsToSD.Transform;
            }

            if (toFrame == "site")
            {
                throw new NotImplementedException("transform to site frame not implemented");
            }

            if (toFrame == "root" || string.IsNullOrEmpty(toFrame))
            {
                return GetTransformToRoot(fromFrame, usePriors, onlyAligned);
            }
            
            var srcToRoot = GetTransformToRoot(fromFrame, usePriors, onlyAligned);
            var dstToRoot = GetTransformToRoot(toFrame, usePriors, onlyAligned);
            return (srcToRoot == null || dstToRoot == null) ? null : srcToRoot.TimesInverse(dstToRoot);
        }

        public UncertainRigidTransform GetObservationTransform(Observation fromObs, Observation toObs,
                                                               bool usePriors = false, bool onlyAligned = false)
        {
            return GetObservationTransform(fromObs, toObs.FrameName, usePriors, onlyAligned);
        }

        /// <summary>
        /// compose transform to root frame
        /// result is null if the transform could not be resolved
        /// if usePriors = true then only prior transform sources will be used
        /// if onlyAligned = true then the result will be null unless at least one transform in the chain is not a prior
        /// </summary>
        public UncertainRigidTransform GetTransformToRoot(Frame frame, bool usePriors = false,
                                                          bool onlyAligned = false)
        {
            var ret = new UncertainRigidTransform(); //identity, no uncertainty

            bool aligned = false;
            for (; frame != null; frame = GetFrame(frame.ParentName))
            {
                var toParent = usePriors ? GetBestPrior(frame) : GetBestTransform(frame);
                if (toParent == null)
                {
                    return null;
                }
                aligned = aligned || !toParent.IsPrior();
                ret = ret * toParent.Transform; //row major transforms compose left to right
            }

            return !onlyAligned || aligned ? ret : null;
        }

        public UncertainRigidTransform GetTransformToRoot(string frameName, bool usePriors = false,
                                                          bool onlyAligned = false)
        {
            return GetTransformToRoot(GetFrame(frameName), usePriors, onlyAligned);
        }
    }
}
