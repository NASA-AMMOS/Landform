using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using JPLOPS.MathExtensions;
using JPLOPS.Util;
using JPLOPS.Imaging;

namespace JPLOPS.Geometry
{
    public enum AtlasMode { None, UVAtlas, Heightmap, Naive, Project, Manifold };

    public static class MeshUVs
    {
        /// <summary>
        /// Remove uvs from this mesh
        /// set all vertex uvs to zero and set meshes HasUVs flag to false
        /// </summary>
        public static void ClearUVs(this Mesh mesh)
        {
            mesh.HasUVs = false;
            foreach (var v in mesh.Vertices)
            {
                v.UV = Vector2.Zero;
            }
        }

        /// <summary>
        /// Returns a bounding box whose min/max represent the component wise minimum and maximum across all vertex uvs
        /// Since min and max are 3D vectors the z components are set to 0
        /// </summary>
        /// <returns></returns>
        public static BoundingBox UVBounds(this Mesh mesh, bool flipY = false)
        {
            if (!mesh.HasUVs)
            {
                throw new Exception("mesh does not have UVs");
            }
            BoundingBox b = new BoundingBox(Vector3.Largest, Vector3.Smallest);
            foreach (Vertex v in mesh.Vertices)
            {
                var uv = new Vector3(v.UV.X, flipY ? (1 - v.UV.Y) : v.UV.Y, 0);
                b.Min = Vector3.Min(b.Min, uv);
                b.Max = Vector3.Max(b.Max, uv);
            }
            return b;
        }

        /// <summary>
        /// remap UV coordinates to the box [border, border]x[1 - border, 1 - border]
        /// </summary>
        public static void RescaleUVs(this Mesh mesh, double border = 0.01, double maxStretch = 1,
                                      double growThreshold = 0.1)
        {
            var targetMin = Vector2.Zero;
            var targetMax = Vector2.One;
            if (border > 0)
            {
                targetMin += border * Vector2.One;
                targetMax -= border * Vector2.One;
            }
            mesh.RescaleUVs(BoundingBoxExtensions.CreateXY(targetMin, targetMax), maxStretch, growThreshold);
        }

        /// <summary>
        /// remap UV coordinates to targetBounds  
        /// </summary>
        /// <param name="maxStretch">
        /// Same semantics as UVAtlas.Atlas(maxStretch). If less than or equal to 0 then force isometric scaling (same
        /// scale factor in each dimension).  If greater than or equal to 1 then allow unlimited aspect ratio of
        /// scaling.  Otherwise limit aspect ratio of scaling to 1/(1-maxStretch).
        /// </param>
        /// <param name="growThreshold">
        /// Ignored if less than or equal to zero.  Otherwise, if the current UV bounds is already contained in
        /// targetBounds and the current bounds size in each dimension is within growThreshold of targetBounds then
        /// don't modify current UV coordinates.
        /// </param>
        public static void RescaleUVs(this Mesh mesh, BoundingBox targetBounds, double maxStretch = 1,
                                      double growThreshold = 0.1)
        {
            if (!mesh.HasVertices)
            {
                return;
            }

            var b = mesh.UVBounds(); //ensures HasUVs=true
            var uvMin = new Vector2(b.Min.X, b.Min.Y);
            var uvMax = new Vector2(b.Max.X, b.Max.Y);
            var sz = uvMax - uvMin;
            var targetSz = targetBounds.Max - targetBounds.Min;

            if (growThreshold > 0 &&
                targetBounds.Contains(b) == ContainmentType.Contains &&
                targetSz.X - sz.X <= growThreshold && targetSz.Y - sz.Y <= growThreshold)
            {
                return;
            }

            double eps = 1e-10;
            var rescale = new Vector2(Math.Abs(sz.X) > eps ? targetSz.X / sz.X : 1,
                                      Math.Abs(sz.Y) > eps ? targetSz.Y / sz.Y : 1);
            if (maxStretch <= 0) //no stretch allowed, force isometric scaling
            {
                if (rescale.X <= rescale.Y)
                {
                    rescale.Y = rescale.X;
                }
                else
                {
                    rescale.X = rescale.Y;
                }
            }
            else if (maxStretch < 1)
            {
                double maxAspect = 1.0 / (1.0 - maxStretch);
                if (rescale.X <= rescale.Y)
                {
                    rescale.Y = Math.Min(rescale.Y, maxAspect * rescale.X);
                }
                else
                {
                    rescale.X = Math.Min(rescale.X, maxAspect * rescale.Y);
                }
            }
            var targetMin = new Vector2(targetBounds.Min.X, targetBounds.Min.Y);
            foreach (Vertex v in mesh.Vertices)
            {
                v.UV = targetMin + (v.UV - uvMin) * rescale;
            }
        }

        /// <summary>
        /// rewrite UVs, if necessary, to fill available texture resolution
        /// </summary>
        public static void RescaleUVsForTexture(this Mesh mesh, int texWidth, int texHeight, double borderPixels = 2,
                                                double maxStretch = 1, double growThreshold = 0.1)
        {
            var border = borderPixels * Vector2.One;
            var targetMin = Image.PixelToUV(border, texWidth, texHeight);
            var targetMax = Image.PixelToUV(new Vector2(texWidth, texHeight) - border, texWidth, texHeight);
            mesh.RescaleUVs(BoundingBoxExtensions.CreateXY(targetMin, targetMax), maxStretch, growThreshold);
        }

        /// <summary>
        /// Clip image to just the area referenced by texture coordinates of this mesh, plus a border.
        /// Then remap the texture coordinates of this mesh in-place to match the clipped image.
        /// Consider TexturedMeshClipper.RemapMeshClipImage() if the mesh uses sparse and disconnected islands in image.
        /// </summary>
        public static Image ClipImageAndRemapUVs(this Mesh mesh, Image image, ref Image index, double borderPixels = 2)
        {
            if (!mesh.HasVertices)
            {
                return image;
            }

            var b = image.UVToPixel(mesh.UVBounds()); //ensures HasUVs=true
            var l = new Vector2(Math.Max(0, b.Min.X - borderPixels), Math.Max(0, b.Min.Y - borderPixels));
            var u = new Vector2(Math.Min(image.Width - 1, b.Max.X + borderPixels),
                                Math.Min(image.Height - 1, b.Max.Y + borderPixels));
            if (l.X == 0 && l.Y == 0 && u.X == image.Width - 1 && u.Y == image.Height - 1)
            {
                return image;
            }

            int startRow = (int)l.Y;
            int startCol = (int)l.X;
            int newWidth = (int)(u.X - l.X + 1);
            int newHeight = (int)(u.Y - l.Y + 1);

            var ret = image.Crop(startRow, startCol, newWidth, newHeight);

            if (index != null)
            {
                index = index.Crop(startRow, startCol, newWidth, newHeight);
            }

            foreach (Vertex v in mesh.Vertices)
            {
                v.UV = ret.PixelToUV(image.UVToPixel(v.UV) - l);
            }

            return ret;
        }

        public static Image ClipImageAndRemapUVs(this Mesh mesh, Image image, double borderPixels = 2)
        {
            Image index = null;
            return mesh.ClipImageAndRemapUVs(image, ref index, borderPixels);
        }

        public static void HeightmapAtlas(this Mesh mesh, Vector3 verticalAxis, bool flipU = false, bool flipV = false,
                                          bool swapUV = false)
        {
            mesh.HeightmapAtlas(BoundingBoxExtensions.GetBoxAxis(verticalAxis), flipU, flipV, swapUV);
        }

        /// <summary>
        /// (re-)assign UVs assuming this mesh is a heightmap
        /// </summary>
        public static void HeightmapAtlas(this Mesh mesh, BoxAxis verticalAxis, bool flipU = false, bool flipV = false,
                                          bool swapUV = false)
        {
            Func<Vector3, Vector2> project = null;
            switch (verticalAxis)
            {
                case BoxAxis.X: project = v => new Vector2(v.Y, v.Z); break;
                case BoxAxis.Y: project = v => new Vector2(v.X, v.Z); break;
                case BoxAxis.Z: project = v => new Vector2(v.X, v.Y); break;
                default: throw new Exception("unknown axis " + verticalAxis);
            }
            var bounds = mesh.Bounds();
            var min = project(bounds.Min);
            var scale = project(bounds.Extent());
            double eps = MathE.EPSILON;
            scale.X = scale.X > eps ? (1 / scale.X) : 1;
            scale.Y = scale.Y > eps ? (1 / scale.Y) : 1;
            foreach (Vertex v in mesh.Vertices)
            {
                v.UV = (project(v.Position) - min) * scale;
                if (flipU)
                {
                    v.UV.X = 1.0 - v.UV.X;
                }
                if (flipV)
                {
                    v.UV.Y = 1.0 - v.UV.Y;
                }
                if (swapUV)
                {
                    v.UV = v.UV.Swap();
                }
                v.UV.X = MathE.Clamp01(v.UV.X);
                v.UV.Y = MathE.Clamp01(v.UV.Y);
            }
            mesh.HasUVs = true;
        }

        public static void NaiveAtlas(this Mesh mesh)
        {
            if (!JPLOPS.Geometry.NaiveAtlas.Compute(mesh, out float[] u, out float[] v, out int[] indices,
                                                 out int[] vertexRemap))
            {
                throw new Exception("naive atlas failed");
            }
            mesh.ApplyAtlas(u, v, indices, vertexRemap);
        }

        /// <summary>
        /// Compute an atlas assuming mesh is a well behaved two-manifold such that the average of all face normals is
        /// likely to be an unoccluded viewing direction.
        ///
        /// Does a rudimentary visibility check to compute the relative area of backfaces from that direction.  However,
        /// there are non-self-intersecting connected two-manfolds without holes that still have no single view
        /// direction without self occlusion (e.g. consider a helicoid).
        /// </summary>
        public static bool ManifoldAtlas(this Mesh mesh, double backfaceAreaRelTol = 0.01)
        {
            if (!mesh.HasFaces)
            {
                return false;
            }
            Vector3 averageFaceNormal = Vector3.Zero;
            int numNonDegenerate = 0;
            double area = 0;
            foreach (var t in mesh.Triangles())
            {
                if (t.TryComputeNormal(out Vector3 tn))
                {
                    averageFaceNormal += tn;
                    numNonDegenerate++;
                }
                area += t.Area();
            }
            if (numNonDegenerate == 0)
            {
                return false;
            }
            averageFaceNormal /= numNonDegenerate;
            averageFaceNormal.Normalize();
            double backfaceAreaThreshold = backfaceAreaRelTol * area;
            double backfaceArea = 0;
            foreach (var t in mesh.Triangles())
            {
                if (t.TryComputeNormal(out Vector3 tn) && Vector3.Dot(tn, averageFaceNormal) < 0)
                {
                    //nondegenerate backface
                    backfaceArea += t.Area();
                    if (backfaceArea > backfaceAreaThreshold)
                    {
                        return false;
                    }
                }
            }
            return mesh.UnoccludedAtlas(-averageFaceNormal);
        }

        public static bool UnoccludedAtlas(this Mesh mesh, Vector3 viewAxis)
        {
            viewAxis.Normalize();
            Vector3 centroid = Vector3.Zero;
            foreach (var v in mesh.Vertices)
            {
                centroid += v.Position;
            }
            centroid /= mesh.Vertices.Count;
            Vector3 extremum = centroid;
            double extremumRadius = 0;
            foreach (var v in mesh.Vertices)
            {
                double r = Vector3.Distance(v.Position, centroid);
                if (r > extremumRadius)
                {
                    extremumRadius =r;
                    extremum = v.Position;
                }
            }
            if (extremumRadius == 0)
            {
                return false;
            }
            Vector3 uAxis = Vector3.Normalize(extremum - centroid);
            Vector3 vAxis = Vector3.Cross(-viewAxis, uAxis);
            if (vAxis.Length() < MathE.EPSILON)
            {
                return false;
            }
            vAxis.Normalize();
            uAxis = Vector3.Cross(vAxis, -viewAxis);
            //rotate 45 deg so that extremum direction is diagonal
            uAxis = 0.5 * (uAxis + vAxis);
            uAxis.Normalize();
            vAxis = Vector3.Cross(-viewAxis, uAxis);
            double minU = double.PositiveInfinity, maxU = double.NegativeInfinity;
            double minV = double.PositiveInfinity, maxV = double.NegativeInfinity;
            foreach (var v in mesh.Vertices)
            {
                var d = v.Position - centroid;
                v.UV = new Vector2(Vector3.Dot(d, uAxis), Vector3.Dot(d, vAxis));
                minU = Math.Min(v.UV.X, minU);
                maxU = Math.Max(v.UV.X, maxU);
                minV = Math.Min(v.UV.Y, minV);
                maxV = Math.Max(v.UV.Y, maxV);
            }
            double rangeU = maxU - minU;
            double rangeV = maxV - minV;
            if (rangeU <= 0 || rangeV <= 0)
            {
                return false;
            }
            foreach (var v in mesh.Vertices)
            {
                v.UV = new Vector2(MathE.Clamp01((v.UV.X - minU) / rangeU), MathE.Clamp01((v.UV.Y - minV) / rangeV));
            }
            mesh.HasUVs = true;
            return true;
        }

        /// <summary>
        /// remap UVs from src to dst, squishing those outside of src  
        /// </summary>
        public static void WarpUVs(this Mesh mesh, BoundingBox src, BoundingBox dst, double ease = 0)
        {
            if (!mesh.HasUVs)
            {
                throw new Exception("mesh does not have UVs");
            }
            var box = BoundingBoxExtensions.CreateXY(0.5 * Vector2.One, 1);
            var warp = box.Create2DWarpFunction(src, dst, ease);
            foreach (Vertex v in mesh.Vertices)
            {
                v.UV = warp(v.UV);
            }
        }

        public static void SwapUVs(this Mesh mesh)
        {
            if (!mesh.HasUVs)
            {
                throw new Exception("mesh does not have UVs");
            }
            foreach (Vertex v in mesh.Vertices)
            {
                v.UV = v.UV.Swap();
            }
        }

        /// <summary>
        /// compute total texture area in pixels covered by this mesh
        /// </summary>
        public static double ComputePixelArea(this Mesh mesh, Image image)
        {
            if (image == null || !mesh.HasUVs)
            {
                return 0;
            }
            double area = 0;
            foreach (var t in mesh.Triangles())
            {
                Vector3 a = new Vector3(image.UVToPixel(t.V0.UV), 0);
                Vector3 b = new Vector3(image.UVToPixel(t.V1.UV), 0);
                Vector3 c = new Vector3(image.UVToPixel(t.V2.UV), 0);
                double triArea = (new Triangle(a, b, c)).Area();
                if (double.IsNaN(triArea))
                {
                    throw new Exception("Triangle area not a number");
                }
                area += triArea;
            }
            return area;
        }

        /// <summary>
        /// compute total texture space area covered by this mesh
        /// </summary>
        public static double ComputeUVArea(this Mesh mesh)
        {
            if (!mesh.HasUVs)
            {
                return 0;
            }
            double area = 0;
            foreach (var t in mesh.Triangles())
            {
                Vector3 a = new Vector3(t.V0.UV, 0);
                Vector3 b = new Vector3(t.V1.UV, 0);
                Vector3 c = new Vector3(t.V2.UV, 0);
                double triArea = (new Triangle(a, b, c)).Area();
                if (double.IsNaN(triArea))
                {
                    throw new Exception("Triangle area not a number");
                }
                area += triArea;
            }
            return area;
        }

        /// <summary>
        /// Apply UV atlas results to a mesh.
        /// </summary>
        public static void ApplyAtlas(this Mesh mesh, float[] u, float[] v, int[] indices, int[] vertexRemap)
        {
            if (indices.Length % 3 != 0)
            {
                throw new ArgumentException("indices not divisible by 3");
            }

            List<Vertex> resVerts = new List<Vertex>(mesh.Vertices.Count);
            for (int i = 0; i < vertexRemap.Length; i++)
            {
                var vert = new Vertex(mesh.Vertices[vertexRemap[i]]);
                vert.UV = new Vector2(u[i], v[i]);
                resVerts.Add(vert);
            }
            mesh.Vertices = resVerts;

            mesh.HasUVs = true;

            List<Face> resFaces = new List<Face>(mesh.Faces.Count);
            for (int i = 0; i < indices.Length; i += 3)
            {
                resFaces.Add(new Face(indices[i], indices[i + 1], indices[i + 2]));
            }
            mesh.Faces = resFaces;

            mesh.Clean();
        }

        public static void XYToUV(this Mesh mesh)
        {
            foreach (Vertex v in mesh.Vertices)
            {
                v.UV = new Vector2(v.Position.X, v.Position.Y);
            }
            mesh.HasUVs = true;
        }

        /// <summary>
        /// add texture coordinates to a mesh by projecting vertices onto an image
        /// also optionally removes any vertices of the mesh that aren't visible in the image
        /// also optionally removes any triangles that are backfacing relative to the camera model
        /// </summary>
        public static void ProjectTexture(this Mesh mesh, Image image, Matrix? meshToImage = null,
                                          bool removeVertsOutsideView = true, bool removeBackfacingTriangles = true,
                                          bool processVertsInParallel = false, bool applyUVs = true,
                                          Action<string> verbose = null)
        {
            if (image.CameraModel == null)
            {
                throw new ArgumentException("image camera model required to project texture");
            }
            mesh.ProjectTexture(image.Width, image.Height, image.CameraModel, meshToImage, removeVertsOutsideView,
                                removeBackfacingTriangles, processVertsInParallel, applyUVs, verbose);
        }

        public static void ProjectTexture(this Mesh mesh, int imgWidth, int imgHeight, CameraModel cameraModel,
                                          Matrix? meshToImage = null, bool removeVertsOutsideView = true,
                                          bool removeBackfacingTriangles = true, bool processVertsInParallel = false,
                                          bool applyUVs = true, Action<string> verbose = null)
        {
            verbose = verbose ?? (msg => {});

            Matrix xform = meshToImage ?? Matrix.Identity;
            ConcurrentBag<Vertex> verticesToRemove = new ConcurrentBag<Vertex>();
            Action<Vertex> generateUV = v =>
            {
                Vector2? px = null;
                double range = -1;
                try
                {
                    px = cameraModel.Project(Vector3.Transform(v.Position, xform), out range);
                }
                catch (Exception)
                {
                    //e.g. CameraModelException: cahvore_3d_to_2d(): too many iterations
                }
                if (range < 0 || !px.HasValue ||
                    px.Value.X < 0 || px.Value.X > (imgWidth - 1) || px.Value.Y < 0 || px.Value.Y > (imgHeight - 1))
                {
                    verticesToRemove.Add(v);
                }
                else if (applyUVs)
                {
                    // TODO: review this half pixel offset
                    //v.UV =  new Vector2((px.Value.X - 0.5) / (image.Width+1),
                    //                    1 - ((px.Value.Y - 0.5) / (image.Height+1)));
                    v.UV = Image.PixelToUV(px.Value, imgWidth, imgHeight);
                    v.UV = Vector2.Clamp(v.UV, Vector2.Zero, Vector2.One);
                }
            };
            if (processVertsInParallel)
            {
                CoreLimitedParallel.ForEach(mesh.Vertices, generateUV);
            }
            else
            {
                mesh.Vertices.ForEach(generateUV);
            }

            mesh.HasUVs |= applyUVs;

            if (removeVertsOutsideView && verticesToRemove.Count > 0)
            {
                int nv = mesh.Vertices.Count;
                int nr = verticesToRemove.Count;
                verbose($"projecting texture: removing {Fmt.KMG(nr)}/{Fmt.KMG(nv)} vertices outside camera model");
                mesh.RemoveVertices(verticesToRemove);
            }

            if (removeBackfacingTriangles && (cameraModel is CAHV))
            {
                Vector3 a = Vector3.Normalize((cameraModel as CAHV).A);
                if (a.Length() > MathE.EPSILON)
                {
                    var camToMesh = Matrix.Invert(xform);
                    a = Vector3.Normalize(Vector3.TransformNormal(a, camToMesh));
                    List<Face> frontFaces = new List<Face>();
                    foreach (var f in mesh.Faces)
                    {
                        var t = mesh.FaceToTriangle(f);
                        if (t.TryComputeNormal(out Vector3 tn) && Vector3.Dot(tn, a) < 0)
                        {
                            frontFaces.Add(f);
                        }
                    }
                    if (frontFaces.Count < mesh.Faces.Count)
                    {
                        int nt = mesh.Faces.Count;
                        int nr = nt - frontFaces.Count;
                        verbose($"projecting texture: removing {Fmt.KMG(nr)}/{Fmt.KMG(nt)} backfaces");
                        mesh.Faces = frontFaces;
                        mesh.RemoveUnreferencedVertices();
                    }
                }
            }
        }
    }
}
