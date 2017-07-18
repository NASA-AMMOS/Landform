using Microsoft.Xna.Framework;
using OPS.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{

    // UNITY - left-handed space used in unity
    //           +Y is real-world up and mars up, +Z is North/forward, +X is East/right

    // LOCAL_LEVEL - right-handed space anchored to a specific site drive on mars.
    //          -Z is mars (and real-world) up, +X is North/forward, +Y is East/right
    //          origin is the "center" of the rover at specifed site/drive 

    /// <summary>
    /// Encapsulate common coordinate frame conversions for Mars rover
    /// </summary>
    public class RoverCoordinateSystem
    {

        /// <summary>
        /// Matrix that converts from unity to local level coordinates
        /// </summary>
        public static readonly Matrix UnityToLocalLevel = new Matrix(0, 1, 0, 0,
                                                                     0, 0, -1, 0,
                                                                     1, 0, 0, 0,
                                                                     0, 0, 0, 1);

        /// <summary>
        /// Matrix that converts from local level to unity coordinates  
        /// </summary>
        public static readonly Matrix LocalLevelToUnity = Matrix.Invert(UnityToLocalLevel);

        /// <summary>
        /// Converts a rover origin rotation as specified in a PDS image metadata to a matrix
        /// </summary>
        /// <param name="roverOriginRotation"></param>
        /// <returns></returns>
        public static Matrix LocalLevelToRover(Quaternion roverOriginRotation)
        {
            return Matrix.CreateFromQuaternion(Quaternion.Inverse(roverOriginRotation));
        }

        /// <summary>
        /// Applies local level to unity transformation to a mesh, includes reverse winding of vertices
        /// </summary>
        /// <param name="localLevelMesh"></param>
        public static void LocalLevelToUnityMesh(Mesh localLevelMesh)
        {
            localLevelMesh.Transform(RoverCoordinateSystem.LocalLevelToUnity);
            localLevelMesh.ReverseWinding();
        }

        /// <summary>
        /// Applies unity to local level transformation to a mesh, includes reverse winding of vertices
        /// </summary>
        /// <param name="localLevelMesh"></param>
        public static void UnityToLocalLevelMesh(Mesh unityMesh)
        {
            unityMesh.Transform(RoverCoordinateSystem.UnityToLocalLevel);
            unityMesh.ReverseWinding();
        }

        /// <summary>
        /// Convert a mesh from site frame to local level given an origin offset vector from the PDS image metadata
        /// </summary>
        /// <param name="siteMesh"></param>
        /// <param name="originOffset"></param>
        public static void SiteToLocalLevelMesh(Mesh siteMesh, Vector3 originOffset)
        {
            siteMesh.Translate(-originOffset);
        }

        /// Convert a mesh from local level to site frame given an origin offset vector from the PDS image metadata
        /// </summary>
        /// <param name="siteMesh"></param>
        /// <param name="originOffset"></param>
        public static void LocalLevelToSiteMesh(Mesh localLevelMesh, Vector3 originOffset)
        {
            localLevelMesh.Translate(originOffset);
        }
    }
}
