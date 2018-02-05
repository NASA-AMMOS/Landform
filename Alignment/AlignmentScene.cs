using OPS.Geometry;
using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public class AlignmentScene
    {
        public SceneNode Root;

        public Dictionary<ImageRef, SceneNode> ImageToNode;
        public List<KeyValuePair<ImageRef, ImageRef>> Overlaps;
        public List<ImagePairCorrespondence> Correspondences;
        
        public AlignmentScene()
        {
            Root = new SceneNode();
            ImageToNode = new Dictionary<ImageRef, SceneNode>();
            Overlaps = new List<KeyValuePair<ImageRef, ImageRef>>();
            Correspondences = new List<ImagePairCorrespondence>();
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
    }
}
