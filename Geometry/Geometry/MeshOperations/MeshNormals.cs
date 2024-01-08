using System;
using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
{
    public static class MeshNormals
    {
        /// <summary>
        /// Generates vertex normals for all vertices based on the sum of the connected face normals
        ///
        /// Ignores degnerate faces.
        /// If all faces incident to a vertex are degenerate, that vertex will have a zero-length normal.
        ///
        /// Call RemoveInvalidFaces() first to avoid that.
        /// </summary>
        public static void GenerateVertexNormals(this Mesh mesh)
        {
            // Start with each vertex normal at 0
            foreach (Vertex vertex in mesh.Vertices)
            {
                vertex.Normal = Vector3.Zero;
            }

            // Calculate each face's normal and add that normal to each point face's points
            foreach (Face face in mesh.Faces)
            {
                Vertex v0 = mesh.Vertices[face.P0];
                Vertex v1 = mesh.Vertices[face.P1];
                Vertex v2 = mesh.Vertices[face.P2];

                if (Triangle.ComputeNormal(v0.Position, v1.Position, v2.Position, out Vector3 faceNormal))
                {
                    v0.Normal += faceNormal;
                    v1.Normal += faceNormal;
                    v2.Normal += faceNormal;
                }
                //otherwise ignore degenerate face
            }

            // Normalize each vertex normal
            mesh.NormalizeNormals();

            // The mesh should now be set as having normals
            mesh.HasNormals = true;
        }

        /// <summary>
        /// Normalize all (non-zero) normals.
        /// </summary>
        public static void NormalizeNormals(this Mesh mesh)
        {
            foreach (Vertex vertex in mesh.Vertices)
            {
                if (vertex.Normal.Length() > MathHelper.Epsilon)
                {
                    vertex.Normal.Normalize();
                }
            }
        }

        /// <summary>
        /// Rescale all (non-zero) normals.
        /// </summary>
        public static void RescaleNormals(this Mesh mesh, Func<double, double> func)
        {
            foreach (Vertex vertex in mesh.Vertices)
            {
                double l = vertex.Normal.Length();
                if (l > MathHelper.Epsilon)
                {
                    double nl = func(l);
                    if (nl != l)
                    {
                        vertex.Normal *= (nl / l);
                    }
                }
            }
        }

        /// <summary>
        /// For each vertex, compute a ray from the vertex to the observation point
        /// If the normal for the vertex points away (more than 90 degrees) from this ray
        /// flip the normal.  This method is useful when points have been captured from a 
        /// sensor and you want to disambiguate normal direction to point toward the sensor
        /// </summary>
        /// <param name="observationPoint"></param>
        public static void FlipNormalsTowardPoint(this Mesh mesh, Vector3 observationPoint)
        {
            foreach (var v in mesh.Vertices)
            {
                Vector3 pointToVert = v.Position - observationPoint;               
                if (Vector3.Dot(v.Normal, pointToVert) > 0)
                {
                    v.Normal *= -1;
                }                
            }
        }

        /// <summary>
        /// Remove vertex normals from this mesh
        /// set all vertex normals to zero and set meshes HasNormals flag to false
        /// </summary>
        public static void ClearNormals(this Mesh mesh)
        {
            mesh.HasNormals = false;
            foreach (var v in mesh.Vertices)
            {
                v.Normal = Vector3.Zero;
            }
        }

        /// <summary>
        /// Returns a bounding box of the component wise minimum and maximum across all vertex normals
        /// </summary>
        /// <returns></returns>
        public static BoundingBox NormalBounds(this Mesh mesh)
        {
            if (!mesh.HasNormals)
            {
                throw new Exception("mesh does not have normals");
            }
            BoundingBox b = new BoundingBox(Vector3.Largest, Vector3.Smallest);
            foreach (Vertex v in mesh.Vertices)
            {
                b.Min = Vector3.Min(b.Min, v.Normal);
                b.Max = Vector3.Max(b.Max, v.Normal);
            }
            return b;
        }
    }
}
