using OPS.Geometry;
using OPS.Imaging;
using OPS.Plumbing;
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
        public MatchingContext Context;
        
        public AlignmentScene()
        {
            Root = new SceneNode();
            ImageToNode = new Dictionary<ImageRef, SceneNode>();
            Context = new MatchingContext();
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
