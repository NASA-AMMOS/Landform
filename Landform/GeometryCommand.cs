using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using CommandLine;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.MathExtensions;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline;

namespace OPS.Landform
{
    public enum AtlasMode { UVAtlas, Heightmap };

    public class GeometryCommandOptions : WedgeCommandOptions
    {
        [Option(HelpText = "Scene mesh coordinate frame: auto, passthrough, newest, oldest, mission_root, project_root, numeric sitedrive SSSDDDD", Default = "auto")]
        public virtual string MeshFrame { get; set; }

        [Option(HelpText = "Scene mesh texture resolution, should be power of two", Default = 8192)]
        public virtual int TextureResolution { get; set; }

        [Option(HelpText = "Max texture charts, 0 for unlimited", Default = 0)]
        public int MaxTextureCharts { get; set; }

        [Option(HelpText = "Max texture stretch, 0 for none, 1 for unlimited", Default = 0.1)]
        public double MaxTextureStretch { get; set; }

        [Option(HelpText = "Min fraction of texture space to use for surface data", Default = 0.5)]
        public double MinSurfaceTextureFraction { get; set; }

        [Option(HelpText = "Disable texture space warp", Default = false)]
        public bool NoTextureWarp { get; set; }

        [Option(HelpText = "Ease texture space warp in range [0, 1], otherwise no easing", Default = 0.5)]
        public double EaseTextureWarp { get; set; }

        [Option(HelpText = "Ease surface pixels per meter factor", Default = 0.2)]
        public double EaseSurfacePPMFactor { get; set; }

        [Option(HelpText = "Orbital sampling rate, non-positive to use DEM resolution", Default = -1)]
        public double OrbitalPointsPerMeter { get; set; }

        [Option(HelpText = "UV generation mode for surface meshes (UVAtlas, Heightmap)", Default = AtlasMode.UVAtlas)]
        public AtlasMode SurfaceUVMode { get; set; }
    }

    public class GeometryCommand : WedgeCommand
    {
        protected GeometryCommandOptions gcopts;

        protected string meshFrame;
        protected int sceneTextureResolution;

        protected double orbitalSamplesPerPixel;

        public GeometryCommand(GeometryCommandOptions gcopts) : base(gcopts)
        {
            this.gcopts = gcopts;
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            //pass null as outDir because we'll be setting it ourselves below
            if (!base.ParseArgumentsAndLoadCaches(null))
            {
                return false; //help
            }

            HandleSpecialMeshFrames();

            SetOutDir(DecorateOutDir(outDir));

            sceneTextureResolution = gcopts.TextureResolution;
            if (!NumberHelper.IsPowerOfTwo(sceneTextureResolution))
            {
                pipeline.LogWarn("scene texture resolution {0} not a power of two", sceneTextureResolution);
            }

            orbitalSamplesPerPixel = 1;
            if (gcopts.OrbitalPointsPerMeter > 0 && orbitalDEMMetersPerPixel > 0)
            {
                orbitalSamplesPerPixel = gcopts.OrbitalPointsPerMeter * orbitalDEMMetersPerPixel;
            }
            
            return true;
        }

        protected override string DecorateOutDir(string outDir)
        {
            return base.DecorateOutDir(string.Format("{0}/{1}Frame", outDir, meshFrame));
        }

        protected virtual string GetMeshFrame()
        {
            return gcopts.MeshFrame;
        }

        protected virtual string GetAutoMeshFrame()
        {
            return "newest";
        }

        protected virtual bool PassthroughMeshFrameAllowed()
        {
            return false;
        }

        protected virtual bool NonPassthroughMeshFrameAllowed()
        {
            return true;
        }

        protected virtual void HandleSpecialMeshFrames()
        {
            meshFrame = GetMeshFrame();

            if (string.IsNullOrEmpty(meshFrame))
            {
                return;
            }

            meshFrame = meshFrame.ToLower().Trim();

            if (meshFrame == "auto")
            {
                meshFrame = GetAutoMeshFrame();
            }
                
            string missionRoot = mission != null ? mission.RootFrameName() : null;

            var specials =
                new string[] { "passthrough", "newest", "oldest", "mission_root", "project_root", missionRoot };

            bool isSiteDrive = SiteDrive.IsSiteDriveString(meshFrame);
            bool isSpecial = !isSiteDrive && specials.Contains(meshFrame);

            if (!isSiteDrive && !isSpecial)
            {
                throw new Exception("unsupported mesh frame: " + meshFrame);
            }

            var origMeshFrame = meshFrame;
            if (meshFrame == "passthrough")
            {
                if (!PassthroughMeshFrameAllowed())
                {
                    throw new Exception("--meshframe=passthrough not allowed");
                }
            }
            else if (!NonPassthroughMeshFrameAllowed())
            {
                throw new Exception("only --meshframe=passthrough allowed");
            }

            if (meshFrame == "mission_root" || meshFrame == missionRoot)
            {
                meshFrame = "root"; //recognized as a meta-name by FrameCache.GetObservationTransform()
            }
            else if (meshFrame == "project_root")
            {
                if (rootSiteDrive == null)
                {
                    //this can happen if there were no frames to load or the frame cache was not loaded
                    throw new Exception("project root output requested but no root site drive");
                }
                if (rootSiteDrive == mission.GetLandingSiteDrive())
                {
                    meshFrame = "root";
                }
                else
                {
                    meshFrame = rootSiteDrive.ToString();
                }
            }
            else if (meshFrame == "newest" || meshFrame == "oldest")
            {
                if (observationCache == null)
                {
                    throw new Exception("observation cache not loaded, cannot resolve special frame: " + meshFrame);
                }
                                              
                var sds = observationCache
                    .GetAllObservations()
                    .Where(obs => obs is RoverObservation)
                    .Select(obs => ((RoverObservation)obs).SiteDrive)
                    .Distinct()
                    .ToArray();

                if (sds.Length == 0)
                {
                    throw new Exception("no sitedrives");
                }

                if (meshFrame == "newest")
                {
                    meshFrame = sds.OrderByDescending(sd => sd).First().ToString();
                }
                else
                {
                    meshFrame = sds.OrderBy(sd => sd).First().ToString();
                }

                isSiteDrive = true;
            }

            //some workflows do not load frame cache, for example updating scene manifest for tactical meshes
            if (isSiteDrive && frameCache != null && !frameCache.ContainsFrame(meshFrame))
            {
                throw new Exception("sitedrive frame not found: " + meshFrame);
            }

            pipeline.LogInfo("scene mesh frame: {0}{1}", meshFrame,
                             origMeshFrame != meshFrame ? " (" + origMeshFrame + ")" : "");
        }

        protected string CheckOutputURL<T>(string url, string defaultFilename, string outDir,
                                           SerializerMap<T> serializerMap = null)
        {
            url = StringHelper.NormalizeUrl(url);
            var ext = StringHelper.GetUrlExtension(url);
            if (serializerMap != null && serializerMap.CheckFormat(ext) == null)
            {
                throw new Exception("unsupported output format " + ext);
            }
            if (url.StartsWith("."))
            {
                url = defaultFilename + url;
            }
            if (pipeline is CloudPipeline)
            {
                if (!url.Contains("://"))
                {
                    url = pipeline.GetStorageUrl(outDir, project.Name, url);
                }
                else if (!url.StartsWith(pipeline.StorageUrlWithVenue))
                {
                    throw new Exception(string.Format("output URL {0} outside cloud storage area", url));
                }
            }
            return url;
        }

        protected virtual Mesh UVAtlasMesh(Mesh mesh, int resolution, string name = null) 
        {
            name = !string.IsNullOrEmpty(name) ? (name + " ") : "";
            string msg = string.Format("atlasing {0}mesh ({1} triangles) with UVAtlas, texture resolution {2}",
                                       name, Fmt.KMG(mesh.Faces.Count), resolution);
            if (mesh.Faces.Count > 20000)
            {
                pipeline.LogInfo(msg);
            }
            else
            {
                pipeline.LogVerbose(msg);
            }

            if (mesh.Faces.Count > 100000)
            {
                //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/902
                pipeline.LogWarn("UVAtlas may not work well on large meshes");
            }

            try
            {
                mesh = UVAtlas.Atlas(mesh, resolution, resolution,
                                     gcopts.MaxTextureCharts, (float)gcopts.MaxTextureStretch);
                if (mesh == null)
                {
                    throw new Exception("unknown");
                }
                return mesh;
            }
            catch (Exception ex)
            {
                pipeline.LogError("error atlasing {0} mesh with UVAtlas: {1}", name, ex.Message);
                return null;
            }
        }

        protected virtual Mesh HeightmapAtlasMesh(Mesh mesh, string name = null)
        {
            name = !string.IsNullOrEmpty(name) ? (name + " ") : "";
            string msg = string.Format("heightmap atlasing {0}mesh ({1} triangles)", name, Fmt.KMG(mesh.Faces.Count));
            if (mesh.Faces.Count > 20000)
            {
                pipeline.LogInfo(msg);
            }
            else
            {
                pipeline.LogVerbose(msg);
            }

            //swap U and V because mission surface frames are typically X north, Y east
            //this doesn't really matter here except that backproject texture images created to match these flipped UVs
            //will have north up and east right in image viewers, matching the orientation of other debug images
            mesh.HeightmapAtlas(BoxAxis.Z, swapUV: true);

            return mesh;
        }

        protected virtual Mesh AtlasMesh(Mesh mesh, int resolution, string name = null)
        {
            switch (gcopts.SurfaceUVMode)
            {
                case AtlasMode.UVAtlas: return UVAtlasMesh(mesh, resolution, name);
                case AtlasMode.Heightmap: return HeightmapAtlasMesh(mesh, name);
                default: throw new ArgumentException("unknown atlas mode: " + gcopts.SurfaceUVMode);
            }
        }

        protected virtual bool TextureProjectionEnabled()
        {
            return false;
        }

        protected Vector2 PointToUV(BoundingBox meshBounds, Vector3 pt)
        {
            //regarding the Swap() see comments in HeightmapAtlasMesh()
            var uvScale = meshBounds.Size().XY().Invert();
            return ((pt.XY() - meshBounds.Min.XY()) * uvScale).Swap();
        }

        protected void ComputeTextureWarp(double extent, double centralExtent, out double srcFrac, out double dstFrac)
        {
            int res = sceneTextureResolution;

            double orbitalExtent = extent - centralExtent;

            double orbitalPPM = 1 / orbitalTextureMetersPerPixel;
            
            int orbitalPixels = (int)(orbitalExtent * orbitalPPM);
            
            int surfacePixels = res - orbitalPixels;

            double ease = gcopts.EaseTextureWarp;
            
            if (ease > 0 && ease < 1)
            {
                //afford more pixels to the orbital periphery to support easing
                //this math is a heruistic
                int opWas = orbitalPixels, spWas = surfacePixels;
                double surfacePPM = surfacePixels / centralExtent;
                double ppmFactor = gcopts.EaseSurfacePPMFactor;
                double ppm = ppmFactor * surfacePPM + (1 - ppmFactor) * orbitalPPM;
                double extentFactor = ease * ease;
                orbitalPixels = (int)(extentFactor * orbitalExtent * ppm + (1 - extentFactor) * orbitalPixels);
                surfacePixels = res - orbitalPixels;
                pipeline.LogInfo("increased orbital pixels from {0} to {1} ({2:F3}->{3:F3}m/px) for ease {4:F3}, " +
                                 "surface pixels {5}->{6} ({7:F3}->{8:F3}m/px)",
                                 opWas, orbitalPixels, 1 / orbitalPPM, orbitalExtent / orbitalPixels,
                                 ease, spWas, surfacePixels, 1 / surfacePPM, centralExtent / surfacePixels);
            }
            
            srcFrac = centralExtent / extent;

            dstFrac = ((double)surfacePixels) / res;

            double min = gcopts.MinSurfaceTextureFraction;
            if (dstFrac < min)
            {
                pipeline.LogInfo("increasing surface texture fraction from {0:F3} to min limit {1:F3}", dstFrac, min);
                dstFrac = min;
            }

            int srcSurfacePixels = (int)(srcFrac * res);
            int dstSurfacePixels = (int)(dstFrac * res);
            int srcOrbitalPixels = res - srcSurfacePixels;
            int dstOrbitalPixels = res - dstSurfacePixels;

            pipeline.LogInfo("warping central {0:F3}m of {1:F3}m (ease {2:F3}), {3}->{4} surface pixels " +
                             "({5:F3}->{6:F3}m/px), {7}->{8} orbital pixels ({9:F3}->{10:F3}m/px)",
                             centralExtent, extent, ease,
                             srcSurfacePixels, dstSurfacePixels,
                             centralExtent / srcSurfacePixels, centralExtent / dstSurfacePixels,
                             srcOrbitalPixels, dstOrbitalPixels,
                             orbitalExtent / srcOrbitalPixels, orbitalExtent / dstOrbitalPixels);
        }
    }
}
