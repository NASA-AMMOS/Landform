using Microsoft.Xna.Framework;
using JPLOPS.Geometry;

namespace JPLOPS.Pipeline
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
        /// Creates a matrix that can be used to transform points from local level frame to rover frame
        /// row major storage and row vector convention ( point * matrix) 
        /// </summary>
        /// <param name="roverOriginRotation">rover origin rotation as specified in a PDS image metadata, caller verify reference frame is site_frame</param>
        /// <returns></returns>
        public static Matrix LocalLevelToRover(Quaternion roverOriginRotation)
        {
            return Matrix.CreateFromQuaternion(Quaternion.Inverse(roverOriginRotation));
        }

        /// <summary>
        /// Creates a matrix that can be used to transform points from rover frame to local level frame
        /// row major storage and row vector convention ( point * matrix) 
        /// </summary>
        /// <param name="roverOriginRotation">rover origin rotation as specified in a PDS image metadata, caller verify reference frame is site_frame</param>
        /// <returns</returns>
        public static Matrix RoverToLocalLevel(Quaternion roverOriginRotation)
        {
            return Matrix.CreateFromQuaternion(roverOriginRotation);
        }

        /// <summary>
        /// Creates a matrix that can be used to transform points from local level frame to site frame
        /// row major storage and row vector convention ( point * matrix) 
        /// </summary>
        /// <param name="originOffset">rover origin offset as specified in a PDS image metadata, caller verify reference frame is site_frame</param>
        /// <returns></returns>
        public static Matrix LocalLevelToSite(Vector3 originOffset)
        {
            return Matrix.CreateTranslation(originOffset);
        }

        /// <summary>
        /// Creates a matrix that can be used to transform points from site frame to local level frame
        /// row major storage and row vector convention ( point * matrix) 
        /// </summary>
        /// <param name="originOffset">rover origin offset as specified in a PDS image metadata, caller verify reference frame is site_frame</param>
        /// <returns></returns>
        public static Matrix SiteToLocalLevel(Vector3 originOffset)
        {
            return Matrix.CreateTranslation(-originOffset);
        }

        /// <summary>
        /// Creates a matrix that can be used to transform points from site frame to rover frame
        /// row major storage and row vector convention ( point * matrix)  
        /// </summary>
        /// <param name="roverOriginRotation">rover origin rotation as specified in a PDS image metadata to a matrix, caller verify reference frame is site_frame</param>
        /// <param name="originOffset">rover origin offset as specified in a PDS image metadata, caller verify reference frame is site_frame</param>
        /// <returns></returns>
        public static Matrix SiteToRover(Quaternion roverOriginRotation, Vector3 originOffset)
        {
            return SiteToLocalLevel(originOffset) * LocalLevelToRover(roverOriginRotation);
        }

        /// <summary>
        /// Creates a matrix that can be used to transform points from rover frame to site frame
        /// row major storage and row vector convention ( point * matrix)
        /// </summary>
        /// <param name="roverOriginRotation">rover origin rotation as specified in a PDS image metadata to a matrix, caller verify reference frame is site_frame</param>
        /// <param name="originOffset">rover origin offset as specified in a PDS image metadata, caller verify reference frame is site_frame</param>
        /// <returns></returns>
        public static Matrix RoverToSite(Quaternion roverOriginRotation, Vector3 originOffset)
        {
            return RoverToLocalLevel(roverOriginRotation) * LocalLevelToSite(originOffset);
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
