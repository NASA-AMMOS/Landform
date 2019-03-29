using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using RTree;
using Supercluster.KDTree;

using System.Diagnostics;

namespace OPS.Geometry
{
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
        RTree<int> faceTree;
        RTree<int> vertexTree;
        RTree<int> uvFaceTree;

        List<Triangle> triangles;
        List<Vertex> vertices;

        public List<Vertex> Vertices
        {
            get {
                return new List<Vertex>(vertices);
            }
        }

        public int CountVertices()
        {
            return vertices.Count;
        }

        public List<Triangle> Triangles
        {
            get
            {
                return new List<Triangle>(triangles);
            }
        }

        public bool HasUVs { get; private set; }
        public bool HasNormals { get; private set; }
        public bool HasColors { get; private set; }
        public bool HasFaces { get; private set; }

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
        public MeshOperator(Mesh mesh, bool buildFaceTree = true, bool buildVertexTree = true, bool buildUVFaceTree = true, int maxEntries = 10, int minEntries = 5)
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
                    vertexTree.Add(vertices[i].Bounds().ToRectangle(), i);
                }
            }
            if(HasUVs && buildUVFaceTree)
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
        public Mesh Clip(BoundingBox box, bool ragged = false)
        {
            Mesh result = null;
            if (this.HasFaces)
            {
                if (faceTree == null)
                {
                    throw new Exception("MeshOperator must have a face tree in order to clip meshes");
                }
                List<Triangle> startingTriangles = faceTree.Intersects(box.ToRectangle()).Select(x => triangles[x]).ToList();
                List<Triangle> resTriangles = new List<Triangle>();
                foreach (Triangle t in startingTriangles)
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
                    throw new Exception("MeshOperator must have a vertex tree in order to clip meshes");
                }

                result = new Mesh(HasNormals, HasUVs, HasColors);
                result.Vertices.AddRange(vertexTree.Intersects(box.ToRectangle()).Select(x => vertices[x]).ToList());
            }
            if (!box.FuzzyContains(result.Bounds(), 1E-5) && !ragged)
            {
                throw new Exception("Clipped mesh exceeds bounding box");
            }
            return result;
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
                throw new Exception("MeshOperator must have a vertex tree in order to count vertices");
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
                    // If clip ever returns a triangle it means there is at least one triangle in the box and we can exit
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

            // find first actual face that intersects point and return interpolated position, null otherwise
            foreach (var triangle in triangleList) {
                b = triangle.UVToBarycentric(uv);
                if (b != null)
                    return b;
            }
            return null;
        }

        public List<Triangle> UVIntersects(BoundingBox box)
        {
            return uvFaceTree.Intersects(box.ToRectangle()).Select(x => triangles[x]).ToList();
        }

        public List<Vertex> NearestVertices(Vector3 p, double nearestDist)
        {
            var min = p - new Vector3(nearestDist);
            var max = p + new Vector3(nearestDist);

            var indices = vertexTree.Intersects(new Rectangle(min.ToFloatArray(), max.ToFloatArray()));
            return indices.Select(i => this.Vertices[i]).ToList();
        }
    }

}
