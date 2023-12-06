using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using JPLOPS.Imaging;
using System.IO;

namespace JPLOPS.Pipeline
{
    /// <summary>
    /// Represents a legacy scene from the original OnSight terrain pipeline
    /// </summary>
    public class LegacyScene
    {
        public SceneNode SkyRoot { get; private set; }
        public SceneNode TerrainRoot { get; private set; }

        /// <summary>
        /// Load a scene from the given directory with the given extent.  Extent can be found in the manifest xml file for the scene.
        /// </summary>
        /// <param name="inputDirectory"></param>
        /// <param name="extent"></param>
        public LegacyScene(string inputDirectory, double extent = 4096, bool onlyIncludeSkyTilesWithTexture = true)
        {
            SkyRoot = new SceneNode("sky");
            TerrainRoot = new SceneNode("terrain");

            string meshFilePattern = "*.bob";
            foreach (string filename in Directory.EnumerateFiles(inputDirectory, meshFilePattern))
            {
                Mesh m = Mesh.Load(filename);
                // Note that bob files traditionally store data in the Unity coordinate frame
                // but with the X flipped (left handed).  We conver them back into right handed here.
                for(int i = 0; i < m.Vertices.Count; i++)
                {
                    m.Vertices[i].Position.X *= -1;
                }
                // We also need to reverse winding when switching handedness
                m.ReverseWinding();
                Image img = null;
                if (File.Exists(MeshFilenameToImageFilename(filename)))
                {
                    img = Image.Load(MeshFilenameToImageFilename(filename));
                }
                if(IsSkyTile(filename) && onlyIncludeSkyTilesWithTexture && img == null)
                {
                    continue;
                }
                string id = FileToId(filename);
                SceneNode root = SkyRoot;
                if (!IsSkyTile(filename))
                {
                    Vector3 v = GetUnityOffsetVector(id, extent);
                    m.Translate(v);
                    root = TerrainRoot;
                }
                for (int i = 0; i < m.Vertices.Count; i++)
                {
                    var uv = m.Vertices[i].UV;
                    m.Vertices[i].UV = new Vector2(uv.X, 1.0 - uv.Y);
                    m.Vertices[i].Normal = Vector3.Zero; // Zero out normals since sometimes they are invalid
                }
                m.ClearNormals();// Turn off normals since sometimes they are invalid
                m.Clean();
                var node = FindOrCreateNode(id, root);
                MeshImagePair pair = new MeshImagePair(m, img);
                node.AddComponent(pair);
            }
            SceneNodeTilingExtensions.ComputeBounds(SkyRoot);
            SceneNodeTilingExtensions.ComputeBounds(TerrainRoot);
        }



        static SceneNode FindOrCreateNode(string id, SceneNode root)
        {
            SceneNode curParent = root;
            SceneNode child = null;
            for (int i = 1; i <= id.Length; i++)
            {
                string idPrefix = id.Substring(0, i);
                child = null;
                foreach (var c in curParent.Children)
                {
                    if (c.Name == idPrefix)
                    {
                        child = c;
                        break;
                    }
                }
                if (child == null)
                {
                    child = new SceneNode(idPrefix, curParent.Transform);
                }
                curParent = child;
            }
            return child;
        }

        static string ParentID(string id)
        {
            return id.Substring(0, id.Length - 1);
        }

        static string FileToId(string filename)
        {
            return Path.GetFileNameWithoutExtension(filename).Remove(0, 1);
        }

        static bool IsSkyTile(string filename)
        {
            return Path.GetFileName(filename)[0] == 'f';
        }

        static string MeshFilenameToImageFilename(string meshFilename)
        {
            if (IsSkyTile(meshFilename))
            {
                return Path.Combine(Path.GetDirectoryName(meshFilename), "s" + FileToId(meshFilename) + "h.png");
            }
            else
            {
                return Path.Combine(Path.GetDirectoryName(meshFilename), "t" + FileToId(meshFilename) + "h.jpg");
            }
        }

        public static Vector3 GetUnityOffsetVector(string id, double totalTerrainExtent)
        {
            Vector3 offset = Vector3.Zero;
            double tileSize = totalTerrainExtent;
            for (int i = 0; i < id.Length; i++)
            {
                char curNum = id[i];
                tileSize /= 2;

                if (curNum == '0' || curNum == '2')
                {
                    offset.X += tileSize / 2;
                }
                if (curNum == '1' || curNum == '3')
                {
                    offset.X -= tileSize / 2;
                }
                if (curNum == '0' || curNum == '1')
                {
                    offset.Z += tileSize / 2;
                }
                if (curNum == '2' || curNum == '3')
                {
                    offset.Z -= tileSize / 2;
                }
            }
            return offset;
        }
    }
}
