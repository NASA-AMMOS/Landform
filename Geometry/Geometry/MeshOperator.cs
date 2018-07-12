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
        RTree<int> faceTree;
        RTree<int> vertexTree;
        RTree<int> uvFaceTree;

        List<Triangle> triangles;
        List<Vertex> vertices;

        public List<Vertex> Vertices
        {
            get {
                Vertex[] res = new Vertex[vertices.Count];
                vertices.CopyTo(res);
                return res.ToList();
            }
        }

        public List<Triangle> Triangles
        {
            get
            {
                Triangle[] res = new Triangle[triangles.Count];
                triangles.CopyTo(res);
                return res.ToList();
            }
        }

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
        public MeshOperator(Mesh mesh)
        {
            faceTree = new RTree<int>(10, 5);
            vertexTree = new RTree<int>(10, 5);
            uvFaceTree = new RTree<int>(10, 5);

            hasUVs = mesh.HasUVs;
            hasNormals = mesh.HasNormals;
            hasColors = mesh.HasColors;
            triangles = mesh.Triangles();
            vertices = mesh.Vertices;
            for(int i = 0; i < triangles.Count; i++)
            {
                faceTree.Add(triangles[i].Bounds().ToRectangle(), i);
            }
            for(int i = 0; i < vertices.Count; i++)
            {
                vertexTree.Add(vertices[i].Bounds().ToRectangle(), i);
            }
            if(hasUVs)
            {
                for(int i = 0; i < triangles.Count; i++)
                {
                    uvFaceTree.Add(triangles[i].UVBounds().ToRectangle(), i);
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
                List<Triangle> startingTriangles = faceTree.Intersects(box.ToRectangle()).Select(x => triangles[x]).ToList();
                List<Triangle> resTriangles = new List<Triangle>();
                foreach (Triangle t in startingTriangles)
                {
                    resTriangles.AddRange(t.Clip(box));
                }
                result = new Mesh(resTriangles, hasNormals, hasUVs, hasColors);
            }
            else
            {
                result = new Mesh(hasNormals, hasUVs, hasColors);
                result.Vertices.AddRange(vertexTree.Intersects(box.ToRectangle()).Select(x => vertices[x]).ToList());                
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
            return faceTree.Intersects(box.ToRectangle()).Count;
        }

        /// <summary>
        /// Return the number of vertices inside the given box
        /// </summary>
        /// <param name="box"></param>
        /// <returns></returns>
        public int CountVertices(BoundingBox box)
        {
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
            if(CountVertices(box) > 0)
            {
                return false;
            }
            // Get a list of faces whose bounds intersect the box
            List<Triangle> faces = faceTree.Intersects(box.ToRectangle()).Select(x => triangles[x]).ToList();
            // Try to clip each face to the box
            foreach (Triangle t in faces)
            {
                // If clip ever returns a triangle it means there is at least one triangle in the box and we can exit
                foreach(Triangle clippedT in t.Clip(box))
                {
                    return false;
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
    }

}
