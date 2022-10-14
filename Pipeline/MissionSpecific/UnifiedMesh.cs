using System;
using System.Collections.Generic;
using System.IO;
using JPLOPS.Util;

namespace JPLOPS.Pipeline
{
    public class UnifiedMesh
    {
        public readonly string Path;
        public readonly HashSet<string> Wedges;

        public UnifiedMesh(string path)
        {
            this.Path = path;
            this.Wedges = new HashSet<string>();
        }

        public static UnifiedMesh Load(string path)
        {
            //#Inventor V2.0 ascii
            //File {name "./wedge/NLF_0000F0606540970_105RASLN0010024000309914_0N00LLJ00.iv"}
            //...
            var ret = new UnifiedMesh(path);
            using (FileStream fs = File.OpenRead(path))
            {
                using (StreamReader sr = new StreamReader(fs))
                {
                    string line = null;
                    while ((line = sr.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.StartsWith("File"))
                        {
                            int start = line.IndexOf('"') + 1;
                            int end = line.LastIndexOf('"') - 1;
                            if (start > 0 && start < line.Length - 1 && end > start && end < line.Length - 1)
                            {
                                string wedge = line.Substring(start, end - start + 1);
                                ret.Wedges.Add(StringHelper.GetLastUrlPathSegment(wedge, stripExtension: true));
                            }
                        }
                    }
                }
            }
            return ret;
        }

        public static Dictionary<SiteDrive, Dictionary<RoverProductCamera, UnifiedMesh>>
            LoadAll(List<string> paths, MissionSpecific mission = null,
                    Dictionary<SiteDrive, Dictionary<RoverProductCamera, UnifiedMesh>> unifiedMeshes = null)
        {
            if (unifiedMeshes == null)
            {
                unifiedMeshes = new Dictionary<SiteDrive, Dictionary<RoverProductCamera, UnifiedMesh>>();
            }
            foreach (var path in paths)
            {
                var id = RoverProductId.Parse(StringHelper.GetLastUrlPathSegment(path, stripExtension: true), mission);
                if (id.IsSingleFrame() || !(id is OPGSProductId))
                {
                    throw new ArgumentException("not a unified mesh: " + path);
                }
                if (!id.IsSingleCamera())
                {
                    throw new ArgumentException("not a single camera unified mesh: " + path);
                }
                if (!id.IsSingleSiteDrive())
                {
                    throw new ArgumentException("not a single site-drive unified mesh: " + path);
                }
                var sd = ((OPGSProductId)id).SiteDrive;
                if (!unifiedMeshes.ContainsKey(sd))
                {
                    unifiedMeshes[sd] = new Dictionary<RoverProductCamera, UnifiedMesh>();
                }
                var um = Load(path);
                if (um.Wedges.Count > 0) //in some test datasets there are empty unified meshes
                {
                    unifiedMeshes[sd][id.Camera] = um;
                }
            }
            return unifiedMeshes;
        }

        public static bool CheckUnifiedMeshProductId(RoverProductId id, MissionSpecific mission = null)
        {
            return id != null && id is UnifiedMeshProductIdBase &&
                ((OPGSProductId)id).Size != RoverProductSize.Thumbnail &&
                !id.IsSingleFrame() && id.IsSingleCamera() && id.IsSingleSiteDrive() &&
                (mission == null || mission.CheckProductId(id));
        }

        public static List<string> CollectLatest(List<string> urls, MissionSpecific mission = null)
        {
            var latest = new Dictionary<SiteDrive, Dictionary<RoverProductCamera, string>>();
            foreach (var url in urls)
            {
                if (StringHelper.GetUrlExtension(url).ToUpper() == ".IV")
                {
                    var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                    var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                    if (CheckUnifiedMeshProductId(id, mission))
                    {
                        //rely on mission.CheckProductId() to allow only unified meshes for the correct cameras
                        //and also to filter linear/nonlinear if only one or the other is allowed
                        var sd = ((OPGSProductId)id).SiteDrive;
                        if (!latest.ContainsKey(sd))
                        {
                            latest[sd] = new Dictionary<RoverProductCamera, string>();
                        }
                        if (!latest[sd].ContainsKey(id.Camera))
                        {
                            latest[sd][id.Camera] = url;
                        }
                        else
                        {
                            var oldUrl = latest[sd][id.Camera];
                            var oldStr = StringHelper.GetLastUrlPathSegment(oldUrl, stripExtension: true);
                            var oldId = RoverProductId.Parse(oldStr, mission);
                            bool preferLinear = mission == null || mission.PreferLinearGeometryProducts();
                            if (oldId.Geometry != id.Geometry &&
                                ((preferLinear && id.Geometry == RoverProductGeometry.Linearized) ||
                                 (!preferLinear && id.Geometry == RoverProductGeometry.Raw)))
                            {
                                latest[sd][id.Camera] = url;
                            }
                            else if (id.GetSol() > oldId.GetSol() ||
                                     (id.GetSol() == oldId.GetSol() && id.Version > oldId.Version))
                            {
                                latest[sd][id.Camera] = url;
                            }
                        }
                    }
                }
            }
            var ret = new List<string>();
            foreach (var sd in latest.Keys)
            {
                foreach (var cam in latest[sd].Keys)
                {
                    ret.Add(latest[sd][cam]);
                }
            }
            return ret;
        }
    }
}
