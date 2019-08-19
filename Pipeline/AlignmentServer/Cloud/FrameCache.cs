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
        /// Get a transform from an observation frame to the corresponding rover, sitedrive, or root frame.
        ///
        /// Also works to get a transform from an observation frame to any other observation or sitedrive frame.
        ///
        /// Note this requires an observation in order to identify an observation frame to start with.  Thus this is not
        /// a general purpose function to get a transform between any two frames.  For that see GetRelativeTransform().
        ///
        /// The reason that an observation is required is to ensure that the "from" frame is really an observation
        /// frame. Currently we have no other way to really know that.  We have a weak naming convention for
        /// observation frames that is of the form <sensor name>_<rover motion counter> but it is not formal enough
        /// that given such a string we can be sure that it identifies an observation frame.
        ///
        /// The reason this function requires an observation frame specifically is so that it can support the
        /// meta-names "rover", "sitedrive", and "root" as the destination frame.  This is possible by relying on
        /// the assumption that the frame tree is structured like this:
        ///
        /// root frame <-- sitedrive frame <-- observation/rover frame
        ///
        /// That is, an observation frame is always a rover frame, the parent of an observation frame is always a
        /// sitedrive frame, and the parent of a sitedrive frame is always the root frame.
        ///
        /// Result is null if the transform could not be resolved.
        ///
        /// If usePriors = true then only prior transform sources will be used.
        ///
        /// If onlyAligned = true then the result is null unless at least one transform in the chain is not a prior.
        /// </summary>
        public UncertainRigidTransform GetObservationTransform(Observation fromObs, string toFrameName,
                                                               bool usePriors = false, bool onlyAligned = false)
        {
            if (toFrameName == "rover" || fromObs.FrameName == toFrameName)
            {
                //go from an observation frame to itself
                return new UncertainRigidTransform(); //identity, no uncertainty
            }

            Frame fromFrame = GetFrame(fromObs.FrameName);
            if (fromFrame == null)
            {
                return null;
            }

            UncertainRigidTransform getTransformToSD(Frame obsFrame)
            {
                var obsToSD = usePriors ? GetBestPrior(obsFrame) : GetBestTransform(obsFrame);
                return (obsToSD == null || (onlyAligned && obsToSD.IsPrior())) ? null : obsToSD.Transform;
            }

            if (toFrameName == "sitedrive" || toFrameName == fromFrame.ParentName)
            {
                //go from an observation frame to its parent sitedrive frame
                return getTransformToSD(fromFrame);
            }

            if (toFrameName == "site")
            {
                throw new NotImplementedException("transform to site frame not implemented");
            }

            if (toFrameName == "root" || string.IsNullOrEmpty(toFrameName))
            {
                return GetTransformToRoot(fromFrame, usePriors, onlyAligned);
            }

            //get here iff destination is
            //(a) a different observation frame than fromObs, either in the same sitedrive or another one
            //(b) a sitedrive frame other than the sitedrive containing fromObs

            Frame toFrame = GetFrame(toFrameName);
            if (toFrame == null)
            {
                return null;
            }

            UncertainRigidTransform srcToLCA = null; //LCA = lowest (i.e. nearest) common ancestor
            UncertainRigidTransform dstToLCA = null;

            if (fromFrame.ParentName == toFrame.ParentName)
            {
                //short-circuit case of going from one observation frame to another in the same sitedrive
                //otherwise we'd build up unnecessary uncertainty going down to root and back up
                srcToLCA = getTransformToSD(fromFrame);
                dstToLCA = getTransformToSD(toFrame);
            }
            else
            {
                srcToLCA = GetTransformToRoot(fromFrame, usePriors, onlyAligned);
                dstToLCA = GetTransformToRoot(toFrame, usePriors, onlyAligned);
            }

            return (srcToLCA == null || dstToLCA == null) ? null : srcToLCA.TimesInverse(dstToLCA);
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
        public UncertainRigidTransform GetTransformToRoot(Frame frame, bool usePriors = false, bool onlyAligned = false)
        {
            var ret = new UncertainRigidTransform(); //identity, no uncertainty

            bool aligned = false;
            for (; frame != null && !string.IsNullOrEmpty(frame.ParentName); frame = GetFrame(frame.ParentName))
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

        public UncertainRigidTransform GetRelativeTransform(Frame srcFrame, Frame dstFrame, bool usePriors = false,
                                                            bool onlyAligned = false)
        {
            if (srcFrame == null)
            {
                return (new UncertainRigidTransform()).TimesInverse(GetTransformToRoot(dstFrame));
            }

            if (dstFrame == null)
            {
                return GetTransformToRoot(srcFrame);
            }

            var srcToRoot = new LinkedList<Frame>();
            for (var f = srcFrame; f != null; f = !string.IsNullOrEmpty(f.ParentName) ? GetFrame(f.ParentName) : null)
            {
                srcToRoot.AddLast(f);
            }

            LinkedListNode<Frame> lca = null;
            bool aligned = false;

            UncertainRigidTransform getTransformToLCA(Frame f, Func<Frame, bool> reachedLCA)
            {
                var toLCA = new UncertainRigidTransform(); //identity, no uncertainty
                for (; f != null; f = !string.IsNullOrEmpty(f.ParentName) ? GetFrame(f.ParentName) : null)
                {
                    if (reachedLCA(f))
                    {
                        break;
                    }
                    var toParent = usePriors ? GetBestPrior(f) : GetBestTransform(f);
                    if (toParent == null)
                    {
                        return null;
                    }
                    aligned = aligned || !toParent.IsPrior();
                    toLCA = toLCA * toParent.Transform; //row major transforms compose left to right
                }
                return toLCA;
            }

            var dstToLCA = getTransformToLCA(dstFrame, f => ((lca = srcToRoot.Find(f)) != null));
            if (dstToLCA == null || lca == null)
            {
                return null;
            }

            var srcToLCA = getTransformToLCA(srcFrame, f => (f == lca.Value));
            if (srcToLCA == null || (onlyAligned && !aligned))
            {
                return null;
            }

            return srcToLCA.TimesInverse(dstToLCA);
        }

        public UncertainRigidTransform GetRelativeTransform(string srcFrame, string dstFrame, bool usePriors = false,
                                                            bool onlyAligned = false)
        {
            return GetRelativeTransform(GetFrame(srcFrame), GetFrame(dstFrame), usePriors, onlyAligned);
        }
    }
}
