using System;
using System.Collections.Generic;
using System.Linq;
using JPLOPS.Geometry;

namespace JPLOPS.Pipeline.AlignmentServer
{
    public class FrameCache
    {
        private readonly PipelineCore pipeline;
        private readonly string projectName;

        //Frame name -> Frame
        private readonly Dictionary<string, Frame> frames = new Dictionary<string, Frame>();

        //Frame name -> Frames
        private readonly Dictionary<string, List<Frame>> children = new Dictionary<string, List<Frame>>();

        //Frame name -> TransformSource -> FrameTransform
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

        public void Remove(Frame frame)
        {
            frames.Remove(frame.Name);
            children.Remove(frame.Name);
            transforms.Remove(frame.Name);
        }

        public void Remove(FrameTransform transform)
        {
            if (transforms.ContainsKey(transform.FrameName))
            {
                transforms[transform.FrameName].Remove(transform.Source);
            }
        }

        /// <summary>
        /// convenience function for the common case of allowing all frames but filtering transforms based on parameters
        /// </summary>
        public int PreloadFilteredTransforms(TransformSource[] priorSources, TransformSource[] adjustedSources,
                                             bool usePriors = false, bool onlyAligned = false)
        {
            Func<FrameTransform, bool> filterPrior =
                   transform => priorSources.Length == 0 || priorSources.Any(s => s == transform.Source);
            Func<FrameTransform, bool> filterAdjusted =
                transform => adjustedSources.Length == 0 || adjustedSources.Any(s => s == transform.Source);

            return Preload(loadTransforms: true, transformFilter: ft =>
                           (!usePriors || ft.IsPrior()) && (!onlyAligned || !ft.IsPrior()) &&
                           ((ft.IsPrior() && filterPrior(ft)) || (!ft.IsPrior() && filterAdjusted(ft))));
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

        public int NumFrames()
        {
            return frames.Count;
        }

        public int NumTransforms()
        {
            return transforms.Count;
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
                if (!frames.ContainsKey(name))
                {
                    return new List<FrameTransform>();
                }
                foreach (var transform in FrameTransform.Find(pipeline, GetFrame(name))) Add(transform);
            }
            return transforms[name].Values;
        }

        public IEnumerable<FrameTransform> GetTransforms(Frame frame)
        {
            return GetTransforms(frame.Name);
        }

        public FrameTransform GetTransform(string transformName)
        {
            if (!FrameTransform.SplitName(transformName, out string frameName, out TransformSource source))
            {
                throw new ArgumentException("invalid transform name: " + transformName);
            }
            return GetTransform(frameName, source);
        }

        public FrameTransform GetTransform(string frameName, TransformSource source)
        {
            var ret = GetTransforms(frameName).Where(t => t.Source == source);
            return ret != null ? ret.FirstOrDefault() : null;
        }

        public FrameTransform GetTransform(Frame frame, TransformSource source)
        {
            return GetTransform(frame.Name, source);
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
            {
                return null;
            }

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
        /// meta-names "observation" (aka "rover"), "sitedrive" (aka "local_level"), "site", and "root"
        /// as the destination frame.  This relies on the assumptions that the frame tree is structured like this:
        ///
        /// root frame <-- sitedrive frame <-- observation frame
        ///
        /// and that
        /// * an observation frame is always a rover frame
        /// * the parent of an observation frame is always a sitedrive (local_level) frame
        /// * the transform prior from observation (rover) frame to sitedrive (local_level) frame
        ///   is the rotation ROVER_COORDINATE_SYSTEM.ORIGIN_ROTATION_QUATERNION in the PDS headers of fromObs
        /// * sitedrive <-- observation was stored as the transform of the observation frame during ingestion
        /// * the parent of a sitedrive frame is always the project root frame
        /// * the transform prior from sitedrive (local_level) frame to project root frame 
        ///   is the translation ROVER_COORDINATE_SYSTEM.ORIGIN_OFFSET_VECTOR in the PDS headers of fromObs
        ///   plus the translation prior of site frame to project root
        /// * root <-- sitedrive was stored as the transform of the sitedrive frame during ingestion
        ///
        /// Typically project root frame is mission root frame (site 1 frame) but for some projects it may be some
        /// other site frame, see discussion in ChainPriors().
        ///
        /// Site frames are not explicitly represented in our frame tree. It is uncommon to use site frame, but one
        /// case is wedge mesh RDRs which are typically in site frame (see Mission.GetTacticalMeshFrame()).
        /// site <-- observation transforms are computed by composing sitedrive <-- observation transform with
        /// site <-- sitedrive from the PDS headers of the observation.
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
            if (toFrameName == "observation" || toFrameName == "rover" || fromObs.FrameName == toFrameName)
            {
                //go from an observation frame to itself
                return new UncertainRigidTransform(); //identity, no uncertainty
            }

            Frame fromFrame = GetFrame(fromObs.FrameName);
            if (fromFrame == null)
            {
                pipeline.LogWarn("no transform from observation frame {0} to frame {1}: observation frame not found",
                                 fromObs.FrameName, toFrameName);
                return null;
            }

            UncertainRigidTransform getTransformToSD(Frame obsFrame)
            {
                var obsToSD = usePriors ? GetBestPrior(obsFrame) : GetBestTransform(obsFrame);
                return (obsToSD == null || (onlyAligned && obsToSD.IsPrior())) ? null : obsToSD.Transform;
            }

            if (toFrameName == "sitedrive" || toFrameName == "local_level" || toFrameName == fromFrame.ParentName)
            {
                //go from an observation frame to its parent sitedrive frame
                var ret = getTransformToSD(fromFrame);
                if (ret == null)
                {
                    pipeline.LogWarn("no transform from observation frame {0} to parent sitedrive", fromObs.FrameName);
                }
                return ret;
            }

            if (toFrameName == "site")
            {
                var ret = getTransformToSD(fromFrame);
                if (ret == null)
                {
                    pipeline.LogWarn("no transform from observation frame {0} to parent sitedrive", fromObs.FrameName);
                }
                //row major transforms compose left to right
                //let the image stay in the LRU cache if it's enabled
                //in practice this case should only occur in tactical mesh procesing, not contextual
                return ret * PDSImage.GetSiteDriveToSiteTransformFromPDS(pipeline.LoadImage(fromObs.Url));
            }

            if (toFrameName == "root" || string.IsNullOrEmpty(toFrameName))
            {
                var ret = GetTransformToRoot(fromFrame, usePriors, onlyAligned);
                if (ret == null)
                {
                    pipeline.LogWarn("no transform from observation frame {0} to root", fromObs.FrameName);
                }
                return ret;
            }

            //get here iff destination is
            //(a) a different observation frame than fromObs, either in the same sitedrive or another one
            //(b) a sitedrive frame other than the sitedrive containing fromObs

            Frame toFrame = GetFrame(toFrameName);
            if (toFrame == null)
            {
                pipeline.LogWarn("no transform from observation frame {0} to frame {1}: destination frame not found",
                                 fromObs.FrameName, toFrameName);
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
                if (srcToLCA == null)
                {
                    pipeline.LogWarn("no transform from observation frame {0} to parent sitedrive", fromObs.FrameName);
                }
                if (dstToLCA == null)
                {
                    pipeline.LogWarn("no transform from destination frame {0} to sitedrive {1}",
                                     toFrameName, toFrame.ParentName);
                }
            }
            else
            {
                srcToLCA = GetTransformToRoot(fromFrame, usePriors, onlyAligned);
                dstToLCA = GetTransformToRoot(toFrame, usePriors, onlyAligned);
                if (srcToLCA == null)
                {
                    pipeline.LogWarn("no transform from observation frame {0} to root", fromObs.FrameName);
                }
                if (dstToLCA == null)
                {
                    pipeline.LogWarn("no transform from destination frame {0} to root", toFrameName);
                }
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

        /// <summary>
        /// The structure of our frame tree is rootFrame <- siteDriveFrame <- observationFrame.
        ///
        /// observationFrame = rover which adds the rover orientation to siteDriveFrame.
        ///
        /// siteDriveFrame is local_level which represents the Mars-aligned XYZ translation of the rover due to driving.
        ///
        /// Normally in IngestPDSImage we attempt to create siteDriveFrame priors that are relative to site 1.
        /// So in that case the root frame for the project is the landing location of the rover.
        /// This is possible if we can get valid priors from PlacesDB for all sitedrives we are ingesting.
        ///
        /// (We can optionally fall back to MSLLocationsDB but that is not as desirable as PlacesDB because
        /// * MSLLocationsDB is really part of MSLICE
        /// * MSLLocationsDB is not guaranteed to have entries for every sitedrive
        /// * MSLLocationsDB entries only contain 2D XY offsets
        /// * though our implementation can add the corresponding Z offset if the orbital basemap DEM is available.)
        ///
        /// However there are important use cases where we cannot get valid PlacesDB priors, such as some field tests.
        /// Or if credentials are not available to access an appropriate PlacesDB venue.
        ///
        /// In those cases IngestPDSImage will only be able to crate "PDS priors" for siteDriveFrames.
        /// Those will only hold the XYZ offset from rover to the parent site frame.
        /// Not only are those not relative to site 1, they are not even relative to a single site.
        ///
        /// Here we try to at least chain them together so that they are all relative to a single site.
        /// That is also not necessarily possible, but if it is, then it is much better than nothing.
        /// Effectively that site frame becomes "root" frame for the project.
        /// That would often be sufficient at least for dev work.
        /// Also, some important workflows ultimately output their results in one of the project's siteDriveFrames.
        /// Which should be possible if all the siteDriveFrames in the project are referenced to some common ancestor.
        /// 
        /// The SITE_COORDINATE_SYSTEM PDS header group makes chaining possible.
        /// It gives the XYZ offset from the site of an observation to the previous site.
        /// Unfortunately, we have observed some datasets where this header is not present.
        /// Also, even when it is available, we can only fully chain if the project contains a contiguous set of sites.
        ///
        /// This function checks if the project contains any siteDriveFrames with only PDS priors.
        /// It also checks if the project involves more than one site.
        /// If both are true, then it tries to create PDSChained priors (which are higher priority than PDS priors).
        /// It picks the earliest site in the project as the root.
        /// Any siteDriveFrames in the first site are left untouched, as their PDS priors already do the job.
        /// All siteDriveFrames in other sites with only PDS priors are then chained back to the first.
        /// This is done in site order.
        /// So as long as the set of sites are contiguous and the SITE_COORDINATE_SYSTEM headers were available
        /// we will always know the translation from the previous site to the first site.
        ///
        /// The pdsSiteOffsets dictionary maps a pair (siteB, siteA) to the transform from siteB to siteA.
        /// It should be passed containing all pairs (N, N-1) where N is a site in the project greater than the first.
        /// On return it will also contain all pairs (N, F) where N and F are sites in the project and F is the first.
        ///
        /// Spews warnings if any attempts to chain fail.
        ///
        /// Any new or updated PDSChained transforms are added to the cache and also persisted to the project database.
        ///
        /// Returns true if chaining was not needed or all attempts to chain succeeded.
        /// </summary>
        public bool ChainPriors(IDictionary<Tuple<int, int>, UncertainRigidTransform> pdsSiteOffsets)
        {
            pipeline.LogInfo("chaining PDS priors...");

            var sdsWithPDSPriors = new HashSet<string>();
            var sites = new SortedSet<int>();
            foreach (var frame in GetAllFrames())
            {
                var parent = frame.ParentName != null ? GetFrame(frame.ParentName) : null;
                if (parent != null && parent.ParentName == null) //parent is root frame -> frame is a siteDriveFrame
                {
                    if (GetBestPrior(frame).Source == TransformSource.PDS)
                    {
                        sdsWithPDSPriors.Add(frame.Name);
                    }
                    sites.Add((new SiteDrive(frame.Name)).Site);
                }
            }

            bool ok = true;
            if (sdsWithPDSPriors.Count > 0 && sites.Count > 1)
            {
                int firstSite = sites.First();
                pipeline.LogInfo("attempting to chain sitedrives with site-relative PDS priors to first site {0}",
                                 firstSite);

                foreach (var sd in sdsWithPDSPriors)
                {
                    int site = (new SiteDrive(sd)).Site;
                    if (site == firstSite)
                    {
                        pipeline.LogInfo("not chaining PDS priors for sitedrive {0}, already relative to site {1}",
                                         sd, firstSite);
                        continue;
                    }

                    var siteToFirst = new Tuple<int, int>(site, firstSite);
                    var siteToPrev = new Tuple<int, int>(site, site - 1);
                    var prevToFirst = new Tuple<int, int>(site - 1, firstSite);

                    var frame = GetFrame(sd);

                    var xform = GetTransform(frame, TransformSource.PDS).Transform; //local_level -> site

                    if (pdsSiteOffsets.ContainsKey(siteToFirst))
                    {
                        xform = xform * pdsSiteOffsets[siteToFirst]; //row major transforms compose left to right
                    }
                    else if (pdsSiteOffsets.ContainsKey(siteToPrev) && pdsSiteOffsets.ContainsKey(prevToFirst))
                    {
                        var xformToFirst = pdsSiteOffsets[siteToPrev] * pdsSiteOffsets[prevToFirst];
                        pdsSiteOffsets[siteToFirst] = xformToFirst;
                        xform = xform * xformToFirst;
                    }
                    else
                    {
                        pipeline.LogWarn("cannot chain PDS prior for site {0} to site {1}", site, firstSite);
                        ok = false;
                        continue;
                    }

                    var ft = GetTransform(frame, TransformSource.PDSChained);
                    if (ft == null)
                    {
                        ft = FrameTransform.Create(pipeline, frame, TransformSource.PDSChained, xform);
                        Add(ft);
                    }
                    else
                    {
                        ft.Transform = xform;
                        ft.Save(pipeline);
                    }

                    bool changed = false;
                    lock (frame.Transforms)
                    {
                        changed = frame.Transforms.Add(ft.Source);
                    }
                    if (changed)
                    {
                        frame.Save(pipeline); //don't hold lock while doing save
                    }

                    pipeline.LogInfo("chained PDS priors for sitedrive {0} to first site {1}", sd, firstSite);
                }
            }

            return ok;
        }

        /// <summary>
        /// Check the quality of priors in this FrameCache.
        ///
        /// Checks for:
        /// * siteDriveFrames with no priors
        /// * siteDriveFrames with only site-relative PDS priors
        /// * siteDriveFrames with PDS priors chained back to the first site in the project
        /// * siteDriveFrames with PlacesDB site but PDS drive offsets
        ///
        /// Spews warning or error messages depending on the badness.
        ///
        /// Returns root site drive (sitedrive parented to root frame with identity transform) as long as the frame tree
        /// is at least connected. If all siteDriveFrames have complete priors then that will be landingSiteDrive.
        /// Otherwise it will be the earliest site in the FrameCache.
        ///
        /// Returns null if the frame tree is disconnected.
        /// </summary>
        public SiteDrive? CheckPriors(SiteDrive landingSiteDrive)
        {
            pipeline.LogInfo("checking priors...");

            var sdsWithNoPriors = new HashSet<string>();
            var sdsWithPDSPriors = new HashSet<string>();
            var sdsWithChainedPriors = new HashSet<string>();
            var sdsWithMixedPriors = new HashSet<string>(); //PlacesDB site offset but PDS local_level offset
            var sdsWithFullPriors = new HashSet<string>();
            SiteDrive? firstSD = null;
            foreach (var frame in GetAllFrames())
            {
                var parent = frame.ParentName != null ? GetFrame(frame.ParentName) : null;
                if (parent != null && parent.ParentName == null) //parent is root frame -> frame is a siteDriveFrame
                {
                    var prior = GetBestPrior(frame);
                    var group = prior == null ? sdsWithNoPriors :
                        prior.Source == TransformSource.PDS ? sdsWithPDSPriors :
                        prior.Source == TransformSource.PDSChained ? sdsWithChainedPriors :
                        prior.Source == TransformSource.PlacesDBSitePDSLocal ? sdsWithMixedPriors :
                        sdsWithFullPriors;
                    group.Add(frame.Name);
                    var sd = new SiteDrive(frame.Name); //ArgumentException if frame.Name not a SiteDrive
                    if (!firstSD.HasValue || sd.Site < firstSD.Value.Site)
                    {
                        firstSD = sd;
                    }
                }
            }

            pipeline.LogInfo("{0} sitedrives with no priors: {1}",
                             sdsWithNoPriors.Count, string.Join(", ", sdsWithNoPriors));
            pipeline.LogInfo("{0} sitedrives with only site-relative PDS priors: {1}",
                             sdsWithPDSPriors.Count, string.Join(", ", sdsWithPDSPriors));
            pipeline.LogInfo("{0} sitedrives with PDS priors chained to earliest site in project: {1}",
                             sdsWithChainedPriors.Count, string.Join(", ", sdsWithChainedPriors));
            pipeline.LogInfo("{0} sitedrives with only PlacesDB site but PDS local_level priors: {1}",
                             sdsWithMixedPriors.Count, string.Join(", ", sdsWithMixedPriors));
            pipeline.LogInfo("{0} sitedrives with full priors: {1}",
                             sdsWithFullPriors.Count, string.Join(", ", sdsWithFullPriors));

            SiteDrive? rootSiteDrive = landingSiteDrive;
            if (sdsWithNoPriors.Count > 0)
            {
                pipeline.LogError("incomplete priors! {0} sitedrives with no priors", sdsWithNoPriors.Count);
                rootSiteDrive = null;
            }
            else if (sdsWithPDSPriors.Count > 0 || sdsWithChainedPriors.Count > 0)
            {
                var baseSDs = new HashSet<SiteDrive>();
                foreach (var sd in sdsWithPDSPriors)
                {
                    baseSDs.Add(new SiteDrive(sd));
                }
                
                if (sdsWithChainedPriors.Count > 0 && firstSD.HasValue)
                {
                    baseSDs.Add(firstSD.Value);
                }
                
                int relativeToLanding = sdsWithFullPriors.Count + sdsWithMixedPriors.Count;

                if (baseSDs.Count > 1)
                {
                    pipeline.LogError("incomplete priors: sitedrives relative to {0} different site frames",
                                      baseSDs.Count);
                    rootSiteDrive = null;
                }
                else if (relativeToLanding > 0 && landingSiteDrive != baseSDs.First())
                {
                    int relativeToFirst = sdsWithPDSPriors.Count + sdsWithChainedPriors.Count;
                    pipeline.LogWarn("incomplete priors: {0} sitedrives relative to sitedrive {1}, " +
                                     "but {2} relative to landing sitedrive {3}",
                                     relativeToFirst, baseSDs.First(), relativeToLanding, landingSiteDrive);
                    rootSiteDrive = null;
                }
                else
                {
                    rootSiteDrive = baseSDs.First();
                    if (rootSiteDrive != landingSiteDrive)
                    {
                        pipeline.LogWarn("incomplete priors: all sitedrives relative to sitedrive {0}, " +
                                         "not landing sitedrive {1}", rootSiteDrive, landingSiteDrive);
                    }
                }
            }

            if (sdsWithMixedPriors.Count > 0)
            {
                pipeline.LogWarn("mixed priors: {0} sitedrives with PlacesDB site offset but PDS local_level",
                                 sdsWithMixedPriors.Count);
            }

            return rootSiteDrive;
        }
    }
}
