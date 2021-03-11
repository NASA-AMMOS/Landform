using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using RTree;
using OPS.Util;
using OPS.MathExtensions;
using OPS.Imaging;

namespace OPS.Geometry
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
        /// <summary>
        /// Note that the RTree source we are using is located here:
        /// https://github.com/yeroo/RTree
        /// The definition of rectangular intersection they are using will return true
        /// if a rectangle is fully contained within another.
        /// </summary>
        private RTree<int> faceTree;
        private RTree<int> vertexTree;
        private RTree<int> uvFaceTree;

        private List<Triangle> triangles;
        private List<Vertex> vertices;

        public int CountVertices()
        {
            return vertices.Count;
        }

        public List<Triangle> Triangles { get { return new List<Triangle>(triangles); } }

        public bool HasUVs { get; private set; }
        public bool HasNormals { get; private set; }
        public bool HasColors { get; private set; }
        public bool HasFaces { get; private set; }

        public bool HasFaceTree { get { return faceTree != null; } }
        public bool HasVertexTree { get { return vertexTree != null; } }
        public bool HasUVFaceTree { get { return uvFaceTree != null; } }

        /// <summary>
        /// Return the bounds of the mesh.  Bounds are cached so this method is fast.
        /// </summary>
        public BoundingBox Bounds
        {
            get; private set;
        }

        /// <summary>
        /// Create a mesh operator and compute accelerated structures
        /// </summary>
        /// <param name="mesh"></param>
        public MeshOperator(Mesh mesh, bool buildFaceTree = true, bool buildVertexTree = true,
                            bool buildUVFaceTree = true, int maxEntries = 10, int minEntries = 5)
        {
            vertices = mesh.Vertices;
            HasUVs = mesh.HasUVs;
            HasNormals = mesh.HasNormals;
            HasColors = mesh.HasColors;
            this.triangles = mesh.Triangles();
            if (buildFaceTree)
            {
                faceTree = new RTree<int>(maxEntries, minEntries);               
            	for(int i = 0; i < triangles.Count; i++)
                {
                    faceTree.Add(triangles[i].Bounds().ToRectangle(), i);
                }
            }
            if (buildVertexTree)
            {
                vertexTree = new RTree<int>(maxEntries, minEntries);
                for(int i = 0; i < vertices.Count; i++)
                {
                    vertexTree.Add(vertices[i].Position.ToRectangle(), i);
                }
            }
            if (HasUVs && buildUVFaceTree)
            {
                uvFaceTree = new RTree<int>(10, 5);
                for(int i = 0; i < triangles.Count; i++)
                {
                    uvFaceTree.Add(triangles[i].UVBounds().ToRectangle(), i);
                }
            }
            this.HasFaces = mesh.Faces.Count > 0;
            this.Bounds = mesh.Bounds();
        }

        /// <summary>
        /// Return a new mesh clipped to the given bounding box
        /// </summary>
        /// <param name="box"></param>
        /// <returns></returns>
        public Mesh Clipped(BoundingBox box, bool ragged = false)
        {
            Mesh result = null;
            if (HasFaces)
            {
                if (faceTree == null)
                {
                    throw new Exception("MeshOperator must have a face tree in order to clip meshes");
                }
                List<Triangle> resTriangles = new List<Triangle>();
                foreach (Triangle t in faceTree.Intersects(box.ToRectangle()).Select(x => triangles[x]))
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
                result = new Mesh(resTriangles, HasNormals, HasUVs, HasColors);
            }
            else
            {
                if (vertexTree == null)
                {
                    throw new Exception("MeshOperator must have a vertex tree in order to clip point clouds");
                }

                result = new Mesh(HasNormals, HasUVs, HasColors);
                result.Vertices.AddRange(vertexTree.Intersects(box.ToRectangle()).Select(x => vertices[x]).ToList());
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
            if (HasFaces)
            {
                if (faceTree == null)
                {
                    throw new Exception("MeshOperator must have a face tree in order to clip meshes");
                }
                foreach (Triangle t in faceTree.Intersects(box.ToRectangle()).Select(x => triangles[x]))
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
                foreach (var v in vertexTree.Intersects(box.ToRectangle()).Select(x => vertices[x].Position))
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
        /// Return the number of faces that are contained within or intersect with the given box
        /// </summary>
        /// <param name="box"></param>
        /// <returns></returns>
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
        /// </summary>
        /// <param name="box"></param>
        /// <returns></returns>
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
        /// </summary>
        /// <param name="box"></param>
        /// <returns></returns>
        public List<Vertex> VerticesIn(BoundingBox box)
        {
            if (vertexTree == null)
            {
                throw new Exception("MeshOperator must have a vertex tree in order to get vertices in box");
            }
            return vertexTree.Intersects(box.ToRectangle()).Select(i => vertices[i]).ToList();
        }

        /// <summary>
        /// A bounding box is empty if no it doesnt contain any vertices and no
        /// faces intersect it.  It is possible to have bounding box that contains
        /// no vertices but still intersects a face.
        /// </summary>
        /// <param name="box"></param>
        /// <returns></returns>
        public bool Empty(BoundingBox box)
        {
            if (!HasFaces || vertexTree != null)
            {
                if(vertexTree == null )
                {
                    throw new Exception("MeshOperator must have a vertex tree in order to check for empty bounding box");
                }
                if (CountVertices(box) > 0)
                {
                    return false;
                }
            }
            if (HasFaces)
            {
                if (faceTree == null)
                {
                    throw new Exception("MeshOperator must have a face tree in order to check for empty bounding box");
                }
                // Get a list of faces whose bounds intersect the box
                List<Triangle> faces = faceTree.Intersects(box.ToRectangle()).Select(x => triangles[x]).ToList();
                // Try to clip each face to the box
                foreach (Triangle t in faces)
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
            var triangleList = uvFaceTree.Intersects(box.ToRectangle()).Select(x => triangles[x]).ToList();

            // position returned by attempt to locate uv in r tree triangle
            BarycentricPoint b;
            int count = 0;

            // find first actual face that intersects point and return interpolated position, null otherwise
            foreach (var triangle in triangleList) {
                b = triangle.UVToBarycentric(uv);
                if (b != null) {
                    yield return b;
                    if(maxCount != 0 && ++count >= maxCount)
                    {
                        break;
                    }
                }
            }
        }

        public List<Triangle> UVIntersects(BoundingBox box)
        {
            return uvFaceTree.Intersects(box.ToRectangle()).Select(x => triangles[x]).ToList();
        }

        public List<int> NearestVertexIndices(Vector3 p, double nearestDist)
        {
            var min = p - new Vector3(nearestDist);
            var max = p + new Vector3(nearestDist);
            return vertexTree.Intersects(new Rectangle(min.ToFloatArray(), max.ToFloatArray()));
        }

        public List<int> NearestVertexIndicesXY(Vector3 p, double nearestDist)
        {
            var min = p - new Vector3(nearestDist);
            var max = p + new Vector3(nearestDist);
            min.Z = Bounds.Min.Z;
            max.Z = Bounds.Max.Z;
            return vertexTree.Intersects(new Rectangle(min.ToFloatArray(), max.ToFloatArray()));
        }

        public List<Vertex> NearestVertices(Vector3 p, double nearestDist)
        {
            var indices = NearestVertexIndices(p, nearestDist);
            var result = new List<Vertex>(indices.Count);
            foreach(var i in indices)
            {
                result.Add(this.vertices[i]);
            }
            return result;
        }

        public List<Vertex> NearestVerticesStrict(Vector3 p, double nearestDist)
        {
            var indices = NearestVertexIndices(p, nearestDist);
            var result = new List<Vertex>(indices.Count);
            double nearestDistSq = nearestDist * nearestDist;
            foreach (var i in indices)
            {
                if ((this.vertices[i].Position - p).LengthSquared() <= nearestDistSq)
                {
                    result.Add(this.vertices[i]);
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
            if (!HasUVs)
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

            if(sorted)
            {
                results.Sort((p1, p2) => p1.Pixel.Y == p2.Pixel.Y ? p1.Pixel.X.CompareTo(p2.Pixel.X) : p1.Pixel.Y.CompareTo(p2.Pixel.Y));
            }

            return results;
        }

        /// <summary>
        /// convenience function that returns a simple subset of the pixels in the resulting texture atlas which were valid for this mesh
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
