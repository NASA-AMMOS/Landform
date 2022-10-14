using System;
using Microsoft.Xna.Framework;
using JPLOPS.MathExtensions;

namespace JPLOPS.Geometry
{
    public enum MeshColor { None, Texture, Normals, NormalMagnitude, Elevation, Curvature, TexCoord, TexU, TexV };
    
    public static class MeshColors
    {
        /// <summary>
        /// Remove colors from this mesh
        /// set all vertex colors to zero and set meshes HasColors flag to false
        /// </summary>
        public static void ClearColors(this Mesh mesh)
        {
            mesh.HasColors = false;
            foreach (var v in mesh.Vertices)
            {
                v.Color = Vector4.Zero;
            }
        }

        /// <summary>
        /// like Image.ApplyStdDevStretch() but operates on the vertex colors of this mesh  
        /// if greyscale=true then the colors are interpreted as greyscale (R = G = B)
        /// </summary>
        public static void ApplyStdDevStretchToColors(this Mesh mesh, bool greyscale = false, double nStddev = 3)
        {
            void applyToChannel(Func<Vertex, double> getter, Action<Vertex, double> setter)
            {
                int n = 0;
                double min = double.PositiveInfinity;
                double max = double.NegativeInfinity;
                double mean = 0;
                foreach (var v in mesh.Vertices)
                {
                    var val = getter(v);
                    mean += val;
                    min = Math.Min(min, val);
                    max = Math.Max(max, val);
                    n++;
                }
                mean /= n;

                double variance = 0;
                foreach (var v in mesh.Vertices)
                {
                    var d = getter(v) - mean;
                    variance += d * d;
                }
                variance /= n;
                double stddev = Math.Sqrt(variance);

                double lower = Math.Max(mean - stddev * nStddev, min);
                double upper = Math.Min(mean + stddev * nStddev, max);

                if (min != max)
                {
                    foreach (var v in mesh.Vertices)
                    {
                        setter(v, MathE.Clamp01((getter(v) - lower) / (upper - lower)));
                    }
                }
            }

            if (greyscale)
            {
                applyToChannel(v => v.Color.X, (v, g) => { v.Color.X = v.Color.Y = v.Color.Z = g; });
            }
            else
            {
                applyToChannel(v => v.Color.X, (v, r) => { v.Color.X = r; });
                applyToChannel(v => v.Color.Y, (v, g) => { v.Color.Y = g; });
                applyToChannel(v => v.Color.Z, (v, b) => { v.Color.Z = b; });
            }
        }

        /// <summary>
        /// set vertex color components as absolute values of normal components
        /// if tiltMode is set then a greyscale color is set instead, see OrganizedPointCloud.NormalToTilt()
        /// up defaults to (0, 0, -1) which corresponds to standard mission frames (e.g. SITE, LOCAL_LEVEL)
        /// </summary>
        public static void ColorByNormals(this Mesh mesh, out double minTilt, out double maxTilt,
                                          TiltMode? tiltMode = null, Vector3? up = null) 
        {
            if (!mesh.HasNormals)
            {
                throw new ArgumentException("cannot color mesh without normals by normals");
            }

            if (up == null)
            {
                up = new Vector3(0, 0, -1);
            }

            minTilt = double.PositiveInfinity;
            maxTilt = double.NegativeInfinity;
            foreach (var v in mesh.Vertices)
            {
                var n = v.Normal;
                if (!tiltMode.HasValue)
                {
                    v.Color.X = Math.Abs(n.X);
                    v.Color.Y = Math.Abs(n.Y);
                    v.Color.Z = Math.Abs(n.Z);
                }
                else
                {
                    var tilt = OrganizedPointCloud.NormalToTilt(n, tiltMode.Value, up.Value);
                    minTilt = Math.Min(minTilt, tilt);
                    maxTilt = Math.Max(maxTilt, tilt);
                    v.Color.X = v.Color.Y = v.Color.Z = tilt;
                }
            }
            mesh.HasColors = true;
        }

        public static void ColorByNormals(this Mesh mesh, TiltMode? tiltMode = null, Vector3? up = null) 
        {
            mesh.ColorByNormals(out double minTilt, out double maxTilt, tiltMode, up);
        }

        /// <summary>
        /// set vertex color components from length of vertex normal
        /// if zeroColor and maxColor are given they define a linear color ramp
        /// otherwise zeroColor = (0, 0, 0) and maxColor = (1, 1, 1)
        /// </summary>
        public static void ColorByNormalMagnitude(this Mesh mesh, out double min, out double max,
                                                  Vector3? zeroColor = null, Vector3? maxColor = null)
        {
            if (!mesh.HasNormals)
            {
                throw new ArgumentException("cannot color mesh without normals by normal magnitude");
            }
            min = double.PositiveInfinity;
            max = double.NegativeInfinity;
            foreach (var v in mesh.Vertices)
            {
                double m = v.Normal.Length();
                min = Math.Min(m, min);
                max = Math.Max(m, max);
            }
            double range = max - min;
            if (zeroColor.HasValue && maxColor.HasValue)
            {
                foreach (var v in mesh.Vertices)
                {
                    double m = v.Normal.Length();
                    double t = (m - min) / range;
                    v.Color.X = t * maxColor.Value.X + (1 - t) * zeroColor.Value.X;
                    v.Color.Y = t * maxColor.Value.Y + (1 - t) * zeroColor.Value.Y;
                    v.Color.Z = t * maxColor.Value.Z + (1 - t) * zeroColor.Value.Z;
                }
            }
            else
            {
                foreach (var v in mesh.Vertices)
                {
                    double m = v.Normal.Length();
                    v.Color.X = v.Color.Y = v.Color.Z = (m - min) / range;
                }
            }
            mesh.HasColors = true;
        }

        public static void ColorByNormalMagnitude(this Mesh mesh, Vector3? zeroColor = null, Vector3? maxColor = null)
        {
            mesh.ColorByNormalMagnitude(out double min, out double max, zeroColor, maxColor);
        }
            
        /// <summary>
        /// compute elevation at each vertex and set it as greyscale vertex color
        /// up defaults to (0, 0, -1) which corresponds to standard mission frames (e.g. SITE, LOCAL_LEVEL)
        /// </summary>
        public static void ColorByElevation(this Mesh mesh, out double min, out double max, bool absolute = false,
                                            Vector3? up = null) 
        {
            if (up == null)
            {
                up = new Vector3(0, 0, -1);
            }

            var ctr = absolute ? new Vector3(0, 0, 0) : mesh.Bounds().Center();

            min = double.PositiveInfinity;
            max = double.NegativeInfinity;
            foreach (var v in mesh.Vertices)
            {
                var elev = (v.Position - ctr).Dot(up.Value);
                v.Color.X = v.Color.Y = v.Color.Z = elev;
                min = Math.Min(min, elev);
                max = Math.Max(max, elev);
            }
            
            mesh.HasColors = true;
        }

        public static void ColorByElevation(this Mesh mesh, bool absolute = false, Vector3? up = null) 
        {
            mesh.ColorByElevation(out double min, out double max, absolute, up);
        }

        /// <summary>
        /// compute approximate max abs curvature at each vertex and set it as greyscale vertex color
        /// the mesh must have normals
        /// </summary>
        public static void ColorByCurvature(this Mesh mesh, out double min, out double max)
        {
            if (!mesh.HasNormals)
            {
                throw new ArgumentException("cannot color mesh without normals by curvature");
            }

            var graph = new EdgeGraph(mesh);

            min = double.PositiveInfinity;
            max = double.NegativeInfinity;
            foreach (var v in graph.GetVertNodes())
            {
                double maxAbsCurvature = 0;
                foreach (var e in v.GetAdjacentEdges())
                {
                    var c = Math.Abs(XNAExtensions.Curvature(v.Position, e.Dst.Position, v.Normal, e.Dst.Normal));
                    maxAbsCurvature = Math.Max(maxAbsCurvature, c);
                }
                v.Color.X = v.Color.Y = v.Color.Z = maxAbsCurvature;
                min = Math.Min(min, maxAbsCurvature);
                max = Math.Max(max, maxAbsCurvature);
            }

            mesh.HasColors = true;
        }

        public static void ColorByCurvature(this Mesh mesh)
        {
            ColorByCurvature(mesh, out double min, out double max);
        }

        public static void ColorByUV(this Mesh mesh, int uChannel = 0, int vChannel = 1)
        {
            if (!mesh.HasUVs)
            {
                throw new Exception("no texture coordinates");
            }
            foreach (var v in mesh.Vertices)
            {
                v.Color.X = v.Color.Y = v.Color.Z = 0;
                if (uChannel >= 0 && uChannel < 3)
                {
                    v.Color[uChannel] = v.UV.X;
                }
                if (vChannel >= 0 && vChannel < 3)
                {
                    v.Color[vChannel] = v.UV.Y;
                }
            }
            mesh.HasColors = true;
        }

        /// <summary>
        /// set the colors of this mesh according to the specified mode 
        /// does nothing if mode=Texture or mode=None
        /// otherwise if allowAdjustColors=false then result is same as calling ColorBy{Normals,Curvature,Elevation}()
        /// but if allowAdjustColors=true then the resulting colors are optimized
        /// if stretch=true then ApplyStdDevStretchToColors() is called
        /// otherwise if if the resulting colors are greyscale they are normalized to [0,1]
        /// the colors will be greyscale for Curvature and Elevation modes and Normals modes where tiltMode is not None
        /// </summary>
        public static void ColorBy(this Mesh mesh, MeshColor mode, TiltMode tiltMode = TiltMode.None,
                                   bool allowAdjustColors = true, bool stretch = false, double nStddev = 3)
        {
            bool greyscale = false;
            bool adjustColors = false;
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;

            switch (mode)
            {
                case MeshColor.None: break;
                case MeshColor.Texture: break;
                case MeshColor.NormalMagnitude:
                {
                    mesh.ColorByNormalMagnitude(out min, out max);
                    adjustColors = greyscale = true;
                    break;
                }
                case MeshColor.Normals:
                {
                    mesh.ColorByNormals(out min, out max, tiltMode);
                    adjustColors = greyscale = tiltMode != TiltMode.None;
                    break;
                }
                case MeshColor.Curvature:
                {
                    mesh.ColorByCurvature(out min, out max);
                    adjustColors = greyscale = true;
                    break;
                }
                case MeshColor.Elevation:
                {
                    mesh.ColorByElevation(out min, out max);
                    adjustColors = greyscale = true;
                    break;
                }
                case MeshColor.TexCoord:
                {
                    mesh.ColorByUV();
                    adjustColors = false;
                    break;
                }
                case MeshColor.TexU:
                {
                    mesh.ColorByUV(vChannel: -1);
                    adjustColors = false;
                    break;
                }
                case MeshColor.TexV:
                {
                    mesh.ColorByUV(uChannel: -1);
                    adjustColors = false;
                    break;
                }
            }

            if (adjustColors && allowAdjustColors)
            {
                if (stretch)
                {
                    mesh.ApplyStdDevStretchToColors(greyscale, nStddev);
                }
                else if (greyscale)
                {
                    foreach (var v in mesh.Vertices)
                    {
                        v.Color.X = v.Color.Y = v.Color.Z = (v.Color.X - min) / (max - min);
                    }
                }
            }
        }

        public static void SetColor(this Mesh mesh, float[] color)
        {
            foreach (var v in mesh.Vertices)
            {
                v.Color.X = color[0];
                v.Color.Y = color[1];
                v.Color.Z = color[2];
                v.Color.W = color.Length > 3 ? color[3] : 1;
            }
            mesh.HasColors = true;
        }

        public static void SetColor(this Mesh mesh, Vector3 color)
        {
            mesh.SetColor(color.ToFloatArray());
        }

        public static void SetColor(this Mesh mesh, Vector4 color)
        {
            mesh.SetColor(color.ToFloatArray());
        }
    }
}
