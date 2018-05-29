using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
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
        public LegacyScene(string inputDirectory, double extent = 4096)
        {
            SkyRoot = new SceneNode("sky");
            TerrainRoot = new SceneNode("terrain");

            string meshFilePattern = "*.bob";
            foreach (string filename in Directory.EnumerateFiles(inputDirectory, meshFilePattern))
            {
                Mesh m = Mesh.Load(filename);
                Image img = null;
                if (File.Exists(MeshFilenameToImageFilename(filename)))
                {
                    img = Image.Load(MeshFilenameToImageFilename(filename));
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
                m.HasNormals = false; // Turn off normals since sometimes they are invalid
                var node = FindOrCreateNode(id, root);
                MeshImagePair pair = new MeshImagePair(m, img);
                node.AddComponent(pair);
            }
            ComputeBounds(SkyRoot);
            ComputeBounds(TerrainRoot);
        }

        void ComputeBounds(SceneNode root)
        {
            HashSet<SceneNode> curParents = new HashSet<SceneNode>();
            foreach (var leaf in root.Leaves())
            {
                var pair = leaf.GetComponent<MeshImagePair>();
                leaf.GetOrAddComponent<NodeBounds>().Bounds = pair.Mesh.Bounds();
                if (leaf.Parent != null)
                {
                    curParents.Add(leaf.Parent);
                }
            }

            while (curParents.Count > 0)
            {
                HashSet<SceneNode> nextParents = new HashSet<SceneNode>();
                foreach (var p in curParents)
                {
                    p.GetOrAddComponent<NodeBounds>().Bounds = BoundingBoxExtensions.Union(p.Children.Select(c => c.GetOrAddComponent<NodeBounds>().Bounds).ToArray());
                    if (p.Parent != null)
                    {
                        nextParents.Add(p.Parent);
                    }
                }
                curParents = nextParents;
            }
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
                    offset.X -= tileSize / 2;
                }
                if (curNum == '1' || curNum == '3')
                {
                    offset.X += tileSize / 2;
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
