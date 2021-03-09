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
        [Option(HelpText = "Scene mesh texture resolution, should be power of two", Default = 8192)]
        public virtual int TextureResolution { get; set; }

        [Option(HelpText = "Max texture charts, 0 for unlimited", Default = UVAtlas.DEF_MAX_CHARTS)]
        public virtual int MaxTextureCharts { get; set; }

        [Option(HelpText = "Max texture stretch, 0 for none, 1 for unlimited", Default = UVAtlas.DEF_MAX_STRETCH)]
        public virtual double MaxTextureStretch { get; set; }

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

        protected int sceneTextureResolution;
        protected double maxTextureStretch;

        protected double orbitalSamplesPerPixel;

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

            return true;
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
                if (!UVAtlas.Atlas(mesh, resolution, resolution, gcopts.MaxTextureCharts,
                                   maxTextureStretch, logger: pipeline))
                {
                    throw new Exception("failed to atlas mesh with UVAtlas");
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

        //https://github.jpl.nasa.gov/OnSight/Landform/issues/1149
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
    }
}
