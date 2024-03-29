using System;
using CommandLine;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.MathExtensions;
using JPLOPS.Geometry;
using JPLOPS.Pipeline;

namespace JPLOPS.Landform
{
    public class GeometryCommandOptions : WedgeCommandOptions
    {
        [Option(HelpText = "Scene mesh texture resolution, should be power of two", Default = TexturingDefaults.SCENE_TEXTURE_RESOLUTION)]
        public virtual int TextureResolution { get; set; }

        [Option(HelpText = "Max texture charts, 0 for unlimited", Default = UVAtlas.DEF_MAX_CHARTS)]
        public virtual int MaxTextureCharts { get; set; }

        [Option(HelpText = "Max texture stretch, 0 for none, 1 for unlimited", Default = UVAtlas.DEF_MAX_STRETCH)]
        public virtual double MaxTextureStretch { get; set; }

        [Option(HelpText = "Min fraction of texture space to use for surface data", Default = TexturingDefaults.MIN_SURFACE_TEXTURE_FRACTION)]
        public double MinSurfaceTextureFraction { get; set; }

        [Option(HelpText = "Disable texture space warp", Default = false)]
        public bool NoTextureWarp { get; set; }

        [Option(HelpText = "Ease texture space warp in range [0, 1], otherwise no easing", Default = TexturingDefaults.EASE_TEXTURE_WARP)]
        public double EaseTextureWarp { get; set; }

        [Option(HelpText = "Ease surface pixels per meter factor", Default = TexturingDefaults.EASE_SURFACE_PPM_FACTOR)]
        public double EaseSurfacePPMFactor { get; set; }

        [Option(HelpText = "Orbital sampling rate, non-positive to use DEM resolution", Default = -1)]
        public double OrbitalPointsPerMeter { get; set; }

        [Option(HelpText = "UV generation mode for meshes if texture projection is not available (None, UVAtlas, Heightmap, Naive)", Default = TexturingDefaults.ATLAS_MODE)]
        public virtual AtlasMode AtlasMode { get; set; }

        [Option(HelpText = "Max runtime for UVAtlas", Default = 10 * 60)]
        public virtual int MaxUVAtlasSec { get; set; }
    }

    public class GeometryCommand : WedgeCommand
    {
        public const int ATLAS_LOG_THRESHOLD = 50000;
        public const int UVATLAS_WARN_THRESHOLD = 100000;

        protected GeometryCommandOptions gcopts;

        protected int sceneTextureResolution;
        protected double maxTextureStretch;

        protected double orbitalSamplesPerPixel;

        protected int numUVatlas, numHeightmapAtlas, numNaiveAtlas, numManifoldAtlas;

        public GeometryCommand(GeometryCommandOptions gcopts) : base(gcopts)
        {
            this.gcopts = gcopts;
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            if (!base.ParseArgumentsAndLoadCaches(outDir))
            {
                return false; //help
            }

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
            
            maxTextureStretch = gcopts.MaxTextureStretch;
            if (maxTextureStretch < 0 || maxTextureStretch > 1)
            {
                throw new Exception("MaxTextureStretch must be between 0 and 1");
            }

            string atlasMsg = "";
            if (gcopts.AtlasMode == AtlasMode.UVAtlas)
            {
                atlasMsg = $" (max time {Fmt.HMS(gcopts.MaxUVAtlasSec * 1000)}, will fallback to heightmap)";
            }
            else if (gcopts.AtlasMode == AtlasMode.Manifold)
            {
                atlasMsg = $" (will fallback to UVAtlas, max time {Fmt.HMS(gcopts.MaxUVAtlasSec * 1000)}";
                atlasMsg += ", then heightmap)";
            }
            pipeline.LogInfo("atlas mode {0}{1}", gcopts.AtlasMode, atlasMsg);

            return true;
        }

        protected virtual void UVAtlasMesh(Mesh mesh, int resolution, string name = null) 
        {
            string msg =
                string.Format("atlassing {0}mesh ({1} triangles) with UVAtlas, texture resolution {2}",
                              !string.IsNullOrEmpty(name) ? (name + " ") : "", Fmt.KMG(mesh.Faces.Count), resolution);
            if (mesh.Faces.Count > ATLAS_LOG_THRESHOLD)
            {
                pipeline.LogInfo(msg);
            }
            else
            {
                pipeline.LogVerbose(msg);
            }

            if (mesh.Faces.Count > UVATLAS_WARN_THRESHOLD)
            {
                pipeline.LogWarn("UVAtlas may not work well on large meshes");
            }

            if (!UVAtlas.Atlas(mesh, resolution, resolution, gcopts.MaxTextureCharts,
                               maxTextureStretch, logger: pipeline, fallbackToNaive: false,
                               maxSec: gcopts.MaxUVAtlasSec))
            {
                pipeline.LogWarn("failed to atlas {0}mesh with UVAtlas, falling back to heightmap atlas",
                                 !string.IsNullOrEmpty(name) ? (name + " ") : "", Fmt.KMG(mesh.Faces.Count));
                HeightmapAtlasMesh(mesh, name);
            }
            else
            {
                numUVatlas++;
            }
        }

        protected virtual void HeightmapAtlasMesh(Mesh mesh, string name = null)
        {
            string msg = string.Format("heightmap atlassing {0}mesh ({1} triangles)",
                                       !string.IsNullOrEmpty(name) ? (name + " ") : "", Fmt.KMG(mesh.Faces.Count));

            if (mesh.Faces.Count > ATLAS_LOG_THRESHOLD)
            {
                pipeline.LogInfo(msg);
            }
            else
            {
                pipeline.LogVerbose(msg);
            }

            //swap U and V because mission surface frames are typically X north, Y east
            //this doesn't really matter here except that texture images created to match these flipped UVs
            //will have north up and east right in image viewers, matching the orientation of other debug images
            mesh.HeightmapAtlas(BoxAxis.Z, swapUV: true);
            numHeightmapAtlas++;
        }

        protected virtual void NaiveAtlasMesh(Mesh mesh, string name = null)
        {
            string msg = string.Format("naive atlassing {0}mesh ({1} triangles)",
                                       !string.IsNullOrEmpty(name) ? (name + " ") : "", Fmt.KMG(mesh.Faces.Count));
            if (mesh.Faces.Count > ATLAS_LOG_THRESHOLD)
            {
                pipeline.LogInfo(msg);
            }
            else
            {
                pipeline.LogVerbose(msg);
            }
            mesh.NaiveAtlas();
            numNaiveAtlas++;
        }

        protected virtual void ManifoldAtlasMesh(Mesh mesh, string name = null)
        {
            string msg = string.Format("manifold atlassing {0}mesh ({1} triangles)",
                                       !string.IsNullOrEmpty(name) ? (name + " ") : "", Fmt.KMG(mesh.Faces.Count));
            if (mesh.Faces.Count > ATLAS_LOG_THRESHOLD)
            {
                pipeline.LogInfo(msg);
            }
            else
            {
                pipeline.LogVerbose(msg);
            }
            if (!mesh.ManifoldAtlas())
            {
                //this is expected for a non-convex mesh, verbose not info
                pipeline.LogVerbose("failed to manifold atlas {0}mesh, falling back to UVAtlas",
                                    !string.IsNullOrEmpty(name) ? (name + " ") : "");
                UVAtlasMesh(mesh, UVAtlas.DEF_RESOLUTION, name);
            }
            else
            {
                numManifoldAtlas++;
            }
        }

        protected virtual void AtlasMesh(Mesh mesh, int resolution, string name = null)
        {
            AtlasMesh(mesh, resolution, name, gcopts.AtlasMode);
        }

        protected virtual void AtlasMesh(Mesh mesh, int resolution, string name, AtlasMode mode)
        {
            switch (mode)
            {
                case AtlasMode.None: throw new Exception($"cannot atlas mesh, atlassing disabled");
                case AtlasMode.UVAtlas: UVAtlasMesh(mesh, resolution, name); break;
                case AtlasMode.Heightmap: HeightmapAtlasMesh(mesh, name); break;
                case AtlasMode.Project: //fallthrough here, see TextureCommand.AtlasMesh()
                case AtlasMode.Naive: NaiveAtlasMesh(mesh, name); break;
                case AtlasMode.Manifold: ManifoldAtlasMesh(mesh, name); break;
                default: throw new ArgumentException("unsupported atlas mode: " + mode);
            }
        }

        protected virtual void DumpAtlasStats()
        {
            if (numUVatlas > 0)
            {
                pipeline.LogInfo("UVAtlassed {0} meshes", numUVatlas);
            }
            if (numHeightmapAtlas > 0)
            {
                pipeline.LogInfo("heightmap atlassed {0} meshes", numHeightmapAtlas);
            }
            if (numNaiveAtlas > 0)
            {
                pipeline.LogInfo("naive atlassed {0} meshes", numNaiveAtlas);
            }
            if (numManifoldAtlas > 0)
            {
                pipeline.LogInfo("manifold atlassed {0} meshes", numManifoldAtlas);
            }
        }

        protected Vector2 PointToUV(BoundingBox meshBounds, Vector3 pt)
        {
            //regarding the Swap() see comments in HeightmapAtlasMesh()
            var uvScale = meshBounds.Extent().XY().Invert();
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

        public static string ClearMeshType(string idStr, MissionSpecific mission)
        {
            var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
            if (id != null && id.GetMeshTypeSpan(out int s, out int l))
            {
                return idStr.Substring(0, s) + (new String('_', l)) + idStr.Substring(s + l);
            }
            return idStr;
        }

        protected string ClearMeshType(string idStr)
        {
            return ClearMeshType(idStr, mission);
        }

        protected void ClearImageCache()
        {
            pipeline.LogInfo("clearing pipeline image cache");
            pipeline.ClearCaches(clearImageCache: true, clearDataProductCache: false);
            //GC and memory spew will happen at end of RunPhase()
        }
    }
}
