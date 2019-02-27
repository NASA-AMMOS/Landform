using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.Util;
using OPS.Geometry;

namespace OPS.Alignment
{
    public class AlignmentScene
    {
        public SceneNode Root;

        //these are all indexed by observation URL
        public Dictionary<string, SceneNode> ObservationUrlToNode;
        public Dictionary<string, ImageFeature[]> DetectedFeatures;
        public Dictionary<URLPair, ImagePairCorrespondence> Correspondences;
        public HashSet<URLPair> Overlaps;

        public AlignmentScene()
        {
            Root = new SceneNode();
            ObservationUrlToNode = new Dictionary<string, SceneNode>();
            DetectedFeatures = new Dictionary<string, ImageFeature[]>();
            Correspondences = new Dictionary<URLPair, ImagePairCorrespondence>();
            Overlaps = new HashSet<URLPair>();
        }

        public string DebugString()
        {
            StringBuilder sb = new StringBuilder();
            Action<SceneNode, int> collect = null;
            collect = (node, depth) =>
            {
                for (int i = 0; i < depth; i++)
                {
                    sb.Append("  ");
                }
                sb.AppendFormat("o {0} ({1})\n", node.Name, node.Transform.LocalToWorld.Translation.ToString());

                foreach (var child in node.Children)
                {
                    collect(child, depth + 1);
                }
            };
            collect(Root, 0);
            return sb.ToString();
        }

        class JsonNode
        {
            public string Name;
            public string Guid;
            public Vector3 Translation;
            public Quaternion Rotation;

            public List<JsonNode> Children;
            public JsonRay Ray;
            public string PointCloud;
        }

        class JsonRay
        {
            public Vector3 Center;
            public Vector3 Direction;
        }

        public void Save(string path)
        {
            Func<SceneNode, JsonNode> serialize = null;
            serialize = (node) =>
            {
                var res = new JsonNode();
                res.Name = node.Name;
                res.Guid = node.Guid.ToString();
                res.Translation = node.Transform.Translation;
                res.Rotation = node.Transform.Rotation;

                res.Children = new List<JsonNode>();
                if (node.HasComponent<CameraRay>())
                {
                    var rayC = node.GetComponent<CameraRay>();
                    res.Ray = new JsonRay();
                    res.Ray.Center = rayC.Center;
                    res.Ray.Direction = rayC.Direction;
                }
                if (node.HasComponent<PointCloudReference>())
                {
                    res.PointCloud = node.GetComponent<PointCloudReference>().Path;
                }
                foreach (var child in node.Children)
                {
                    res.Children.Add(serialize(child));
                }

                return res;
            };
            JsonNode jsonStructure = serialize(Root);
            File.WriteAllText(path, JsonConvert.SerializeObject(jsonStructure));
        }
    }
}
