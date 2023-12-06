using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Xna.Framework;
using RTree;
using JPLOPS.Util;
using JPLOPS.MathExtensions;
using JPLOPS.Imaging;

namespace JPLOPS.Geometry
{
    //potentially huge lists of these things are thrown around in backproject
    //let's keep memory usage down by making it a class (reference type) not a struct (value type)
    public class PixelPoint
    {
        public Vector2 Pixel;
        public Vector3 Point;
        public PixelPoint(Vector2 pixel, Vector3 point)
        {
            this.Pixel = pixel;
            this.Point = point;
        }
    }

    /// <summary>
    /// A class for performing optimized operations on a mesh
    /// Internally this class generates and caches datastructures such as KDTrees
    /// to accelerate certain types of mesh operations
    /// </summary>
    public class MeshOperator
    {
        public readonly Mesh Mesh;

        public bool HasFaceTree { get { return faceTree != null; } }
        public bool HasVertexTree { get { return vertexTree != null; } }
        public bool HasUVFaceTree { get { return uvFaceTree != null; } }

        public int FaceCount { get { return Mesh.Faces.Count; } }
        public int VertexCount { get { return Mesh.Vertices.Count; } } //includes verts not referenced by faces

        public BoundingBox Bounds { get; private set; } //precomputed

        public Triangle GetTriangle(int index)
        {
            return Mesh.FaceToTriangle(index);
        }

        /// <summary>
        /// Note that the RTree source we are using is located here:
        /// https://github.com/yeroo/RTree
        /// The definition of rectangular intersection they are using will return true
        /// if a rectangle is fully contained within another.
        /// </summary>
        private RTree<int> faceTree;
        private RTree<int> vertexTree;
        private RTree<int> uvFaceTree;

        /// <summary>
        /// Create a mesh operator and compute accelerated structures
        /// </summary>
        public MeshOperator(Mesh mesh, bool buildFaceTree = true, bool buildVertexTree = true,
                            bool buildUVFaceTree = true, int maxEntries = 10, int minEntries = 5)
        {
            this.Mesh = mesh;

            var bounds = new BoundingBox(Vector3.Largest, Vector3.Smallest);

            if (buildVertexTree)
            {
                vertexTree = new RTree<int>(maxEntries, minEntries);
            }
            
            for(int i = 0; i < VertexCount; i++)
            {
                bounds.Min = Vector3.Min(bounds.Min, Mesh.Vertices[i].Position);
                bounds.Max = Vector3.Max(bounds.Max, Mesh.Vertices[i].Position);

                if (vertexTree != null)
                {
                    vertexTree.Add(Mesh.Vertices[i].Position.ToRectangle(), i);
                }
            }

            this.Bounds = bounds;

            if (buildFaceTree)
            {
                faceTree = new RTree<int>(maxEntries, minEntries);               
            }

            if (Mesh.HasUVs && buildUVFaceTree)
            {
                uvFaceTree = new RTree<int>(maxEntries, minEntries);
            }

            if (faceTree != null || uvFaceTree != null)
            {
            	for(int i = 0; i < FaceCount; i++)
                {
                    var tri = GetTriangle(i);
                    if (faceTree != null)
                    {
                        faceTree.Add(tri.Bounds().ToRectangle(), i);
                    }
                    if (uvFaceTree != null)
                    {
                        uvFaceTree.Add(tri.UVBounds().ToRectangle(), i);
                    }
                }
            }
        }

        /// <summary>
        /// Return a new mesh clipped to the given bounding box
        /// </summary>
        public Mesh Clipped(BoundingBox box, bool ragged = false)
        {
            Mesh result = null;
            if (Mesh.HasFaces)
            {
                if (faceTree == null)
                {
                    throw new Exception("MeshOperator must have a face tree in order to clip meshes");
                }
                List<Triangle> resTriangles = new List<Triangle>();
                foreach (Triangle t in faceTree.Intersects(box.ToRectangle()).Select(x => GetTriangle(x)))
                {
                    if (ragged)
                    {
                        resTriangles.Add(t);
                    }
                    else
                    {
                        resTriangles.AddRange(t.Clip(box));
                    }
                }
                result = new Mesh(resTriangles, Mesh.HasNormals, Mesh.HasUVs, Mesh.HasColors);
            }
            else
            {
                if (vertexTree == null)
                {
                    throw new Exception("MeshOperator must have a vertex tree in order to clip point clouds");
                }

                result = new Mesh(Mesh.HasNormals, Mesh.HasUVs, Mesh.HasColors);
                result.Vertices.AddRange(vertexTree
                                         .Intersects(box.ToRectangle())
                                         .Select(x => Mesh.Vertices[x])
                                         .ToList());
            }
            if (result.HasVertices && !box.FuzzyContains(result.Bounds(), 1E-5) && !ragged)
            {
                throw new Exception("Clipped mesh exceeds bounding box");
            }
            return result;
        }   

        /// <summary>
        /// compute the bounds that a mesh from a corresponding call to Clip() would have
        /// </summary>
        public BoundingBox ClippedMeshBounds(BoundingBox box, bool ragged = false)
        {
            BoundingBox ret = BoundingBoxExtensions.CreateEmpty();
            if (Mesh.HasFaces)
            {
                if (faceTree == null)
                {
                    throw new Exception("MeshOperator must have a face tree in order to clip meshes");
                }
                foreach (Triangle t in faceTree.Intersects(box.ToRectangle()).Select(x => GetTriangle(x)))
                {
                    if (ragged)
                    {
                        BoundingBoxExtensions.Extend(ref ret, t);
                    }
                    else
                    {
                        foreach (var ct in t.Clip(box))
                        {
                            BoundingBoxExtensions.Extend(ref ret, ct);
                        }
                    }
                }
            }
            else
            {
                if (vertexTree == null)
                {
                    throw new Exception("MeshOperator must have a vertex tree in order to clip point clouds");
                }
                foreach (var v in vertexTree.Intersects(box.ToRectangle()).Select(x => Mesh.Vertices[x].Position))
                {
                    BoundingBoxExtensions.Extend(ref ret, v);
                }
            }
            if (!box.FuzzyContains(ret, 1E-5) && !ragged)
            {
                throw new Exception("clipped mesh bounds exceeds bounding box");
            }
            return ret;
        }

        /// <summary>
        /// compute the area that a mesh from a corresponding call to Clip() would have
        /// </summary>
        public double ClippedMeshArea(BoundingBox box, bool ragged = false)
        {
            if (!Mesh.HasFaces)
            {
                return 0;
            }
            if (faceTree == null)
            {
                throw new Exception("MeshOperator must have a face tree in order to clip meshes");
            }
            double area = 0;
            foreach (Triangle t in faceTree.Intersects(box.ToRectangle()).Select(x => GetTriangle(x)))
            {
                if (ragged)
                {
                    area += t.Area();
                }
                else
                {
                    foreach (var ct in t.Clip(box))
                    {
                        area += t.Area();
                    }
                }
            }
            return area;
        }

        /// <summary>
        /// Return the number of faces that are contained within or intersect with the given box
        /// </summary>
        public int CountFaces(BoundingBox box)
        {
            if (faceTree == null)
            {
                throw new Exception("MeshOperator must have a face tree in order to count faces");
            }
            return faceTree.Intersects(box.ToRectangle()).Count;
        }

        /// <summary>
        /// Return the number of vertices inside the given box
        /// includes vertices not referenced by any faces
        /// </summary>
        public int CountVertices(BoundingBox box)
        {
            if (vertexTree == null)
            {
                throw new Exception("MeshOperator must have a vertex tree in order to count vertices");
            }
            return vertexTree.Intersects(box.ToRectangle()).Count;
        }

        /// <summary>
        /// Return the vertices inside the given box
        /// includes vertices not referenced by any faces
        /// </summary>
        public List<Vertex> VerticesIn(BoundingBox box)
        {
            if (vertexTree == null)
            {
                throw new Exception("MeshOperator must have a vertex tree in order to get vertices in box");
            }
            return vertexTree.Intersects(box.ToRectangle()).Select(i => Mesh.Vertices[i]).ToList();
        }

        /// <summary>
        /// A bounding box is empty if no it doesnt contain any vertices and no
        /// faces intersect it.  It is possible to have bounding box that contains
        /// no vertices but still intersects a face.
        /// </summary>
        public bool Empty(BoundingBox box)
        {
            if (!Mesh.HasFaces || vertexTree != null)
            {
                if (vertexTree == null )
                {
                    throw new Exception("MeshOperator must have a vertex tree to check for empty bounding box");
                }
                if (CountVertices(box) > 0)
                {
                    return false;
                }
            }
            if (Mesh.HasFaces)
            {
                if (faceTree == null)
                {
                    throw new Exception("MeshOperator must have a face tree in order to check for empty bounding box");
                }
                // Get a list of faces whose bounds intersect the box
                // Try to clip each face to the box
                foreach (Triangle t in faceTree.Intersects(box.ToRectangle()).Select(x => GetTriangle(x)))
                {
                    // If clip ever returns a triangle it means there is at least one triangle in the box
                    foreach (Triangle clippedT in t.Clip(box))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Returns the barycentric position in the first face intersected by the point in uv space, null otherwise
        /// </summary>
        /// <param name="uv"></param>
        /// <returns></returns>
        public BarycentricPoint UVToBarycentric(Vector2 uv)
        {
            var points = UVToBarycentricList(uv, 1);
            return points.Count() > 0 ? points.First() : null;
        }

        /// <summary>
        /// Returns the barycentric positions in all faces intersected by the point in uv space
        /// </summary>
        /// <param name="uv"></param>
        /// <returns></returns>
        public IEnumerable<BarycentricPoint> UVToBarycentricList(Vector2 uv, int maxCount=0)
        {
            if (uvFaceTree == null)
            {
                throw new Exception("MeshOperator must have a uv face tree to convert UV to barycentric");
            }
            // convert the 2d point to bounding box
            BoundingBox box = new BoundingBox(
                new Vector3(uv, 0), 
                new Vector3(uv, 0));

            // get all intersected faces in r tree (based on face bounding boxes)

            // position returned by attempt to locate uv in r tree triangle
            BarycentricPoint b;
            int count = 0;

            // find first actual face that intersects point and return interpolated position, null otherwise
            foreach (var t in uvFaceTree.Intersects(box.ToRectangle()).Select(x => GetTriangle(x)))
            {
                b = t.UVToBarycentric(uv);
                if (b != null) {
                    yield return b;
                    if (maxCount != 0 && ++count >= maxCount)
                    {
                        break;
                    }
                }
            }
        }

        public IEnumerable<Triangle> UVIntersects(BoundingBox box)
        {
            return uvFaceTree.Intersects(box.ToRectangle()).Select(x => GetTriangle(x));
        }

        /// includes vertices not referenced by any faces
        public List<int> NearestVertexIndices(Vector3 p, double nearestDist)
        {
            var min = p - new Vector3(nearestDist);
            var max = p + new Vector3(nearestDist);
            return vertexTree.Intersects(new Rectangle(min.ToFloatArray(), max.ToFloatArray()));
        }

        /// includes vertices not referenced by any faces
        public List<int> NearestVertexIndicesXY(Vector3 p, double nearestDist)
        {
            var min = p - new Vector3(nearestDist);
            var max = p + new Vector3(nearestDist);
            min.Z = Bounds.Min.Z;
            max.Z = Bounds.Max.Z;
            return vertexTree.Intersects(new Rectangle(min.ToFloatArray(), max.ToFloatArray()));
        }

        /// includes vertices not referenced by any faces
        public List<Vertex> NearestVertices(Vector3 p, double nearestDist)
        {
            var indices = NearestVertexIndices(p, nearestDist);
            var result = new List<Vertex>(indices.Count);
            foreach(var i in indices)
            {
                result.Add(Mesh.Vertices[i]);
            }
            return result;
        }

        /// includes vertices not referenced by any faces
        public List<Vertex> NearestVerticesStrict(Vector3 p, double nearestDist)
        {
            var indices = NearestVertexIndices(p, nearestDist);
            var result = new List<Vertex>(indices.Count);
            double nearestDistSq = nearestDist * nearestDist;
            foreach (var i in indices)
            {
                if ((Mesh.Vertices[i].Position - p).LengthSquared() <= nearestDistSq)
                {
                    result.Add(Mesh.Vertices[i]);
                }
            }
            return result;
        }

        /// <summary>
        /// returns all the center locations of pixels (paired with the mesh points) that had valid texels in the atlas
        /// </summary>
        /// <param name="textureResolution">resolution of texture to collect points for</param>        
        public List<PixelPoint> SampleUVSpace(int widthPixels, int heightPixels, bool sorted = false)
        {
            if (!Mesh.HasUVs)
            {
                throw new Exception("mesh needs uvs to sample uv space");
            }

            var pixelToPoint = new ConcurrentDictionary<Vector2, Vector3>();
            int numPixels = widthPixels * heightPixels;
            CoreLimitedParallel.For(0, numPixels, pixel => {

                    int row = pixel / widthPixels;
                    int col = pixel % widthPixels;

                    //half pixel offset applied because we are testing if there would be mesh coverage at the location
                    //we would be sampling at, the center of the pixel
                    Vector2 pixelCenter = Image.ApplyHalfPixelOffset(row, col);
                    Vector2 destPixelUV = Image.PixelToUV(pixelCenter, widthPixels, heightPixels);
                    
                    BarycentricPoint baryPt = UVToBarycentric(destPixelUV); 
                    if (baryPt != null)
                    {
                        Vector2 key = Image.UVToPixel(destPixelUV, widthPixels, heightPixels);
                        pixelToPoint.AddOrUpdate(key, _ => baryPt.Position, (_, __) => baryPt.Position);
                    }
                });

            var results = pixelToPoint.Select(entry => new PixelPoint(entry.Key, entry.Value)).ToList();

            if (sorted)
            {
                results.Sort((p1, p2) => p1.Pixel.Y == p2.Pixel.Y ? p1.Pixel.X.CompareTo(p2.Pixel.X) :
                             p1.Pixel.Y.CompareTo(p2.Pixel.Y));
            }

            return results;
        }

        /// <summary>
        /// convenience function that returns a simple subset of the pixels in the resulting texture atlas which were
        /// valid for this mesh
        /// </summary>
        public List<PixelPoint> SubsampleUVSpace(double pct, int widthPixels, int heightPixels)
        {
            if (pct >= 1.0)
            {
                throw new Exception("expecting to subsample uv space, a percentage >= 1 was passed");
            }

            if (pct <= 0)
            {
                throw new Exception("valid subsample pcts need to be greater than zero");
            }

            List<PixelPoint> pts = SampleUVSpace(widthPixels, heightPixels, true);

            //simple sample which skips enough points to return the requested amount of points
            int subsampledPts = Math.Max(1, (int)(pts.Count * pct));
            int skipPoints = pts.Count / subsampledPts;
            return pts.Where((pt, index) => index % skipPoints == 0).ToList();
        }
    }
}
