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
    /// Internally this class generates and caches datastructhres such as KDTrees
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
        RTree<Triangle> faceTree;
        RTree<Vertex> vertexTree;
        RTree<Triangle> uvFaceTree;
        public List<Triangle> Triangles { get; private set; }


        bool hasUVs;
        bool hasNormals;
        bool hasColors;
        bool hasFaces;

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
            hasUVs = mesh.HasUVs;
            hasNormals = mesh.HasNormals;
            hasColors = mesh.HasColors;
            this.Triangles = mesh.Triangles();

            if (buildFaceTree)
            {
                faceTree = new RTree<Triangle>(maxEntries, minEntries);               
            	foreach(var t in Triangles)
                {
                    faceTree.Add(t.Bounds().ToRectangle(), t);
                }
            }
            if (buildVertexTree)
            {
                vertexTree = new RTree<Vertex>(maxEntries, minEntries);
                foreach (var v in mesh.Vertices)
                {
                    vertexTree.Add(v.Bounds().ToRectangle(), v);
                }
            }
            if(hasUVs && buildUVFaceTree)
            {
                uvFaceTree = new RTree<Triangle>(maxEntries, minEntries);
                foreach (var t in Triangles)
                {
                    uvFaceTree.Add(t.UVBounds().ToRectangle(), t);
                }
            }
            this.hasFaces = mesh.Faces.Count > 0;
            this.Bounds = mesh.Bounds();
        }
        
        /// <summary>
        /// Return a new mesh clipped to the given bounding box
        /// </summary>
        /// <param name="box"></param>
        /// <returns></returns>
        public Mesh Clip(BoundingBox box)
        {
            Mesh result = null;
            if (this.hasFaces)
            {
                if (faceTree == null)
                {
                    throw new Exception("MeshOperator must have a face tree in order to clip meshes");
                }
                List<Triangle> startingTriangles = faceTree.Intersects(box.ToRectangle());
                List<Triangle> resTriangles = new List<Triangle>();
                foreach (Triangle t in startingTriangles)
                {
                    resTriangles.AddRange(t.Clip(box));
                }
                result = new Mesh(resTriangles, hasNormals, hasUVs, hasColors);
            }
            else
            {
                if (vertexTree == null)
                {
                    throw new Exception("MeshOperator must have a vertex tree in order to clip meshes");
                }
                result = new Mesh(hasNormals, hasUVs, hasColors);
                result.Vertices.AddRange(vertexTree.Intersects(box.ToRectangle()));                
            }
            if (!box.FuzzyContains(result.Bounds(), 1E-5))
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
        /// A bounding box is empty if no it doesnt contain any vertices and no
        /// faces intersect it.  It is possible to have bounding box that contains
        /// no vertices but still intersects a face.
        /// </summary>
        /// <param name="box"></param>
        /// <returns></returns>
        public bool Empty(BoundingBox box)
        {
            if (!hasFaces || vertexTree != null)
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
            if (hasFaces)
            {
                if (faceTree == null)
                {
                    throw new Exception("MeshOperator must have a face tree in order to check for empty bounding box");
                }
                // Get a list of faces whose bounds intersect the box
                List<Triangle> faces = faceTree.Intersects(box.ToRectangle());
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
            var triangleList = uvFaceTree.Intersects(box.ToRectangle());

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
            return uvFaceTree.Intersects(box.ToRectangle());
        }
    }

}
