using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public class SceneGraph
    {
        public SceneGraph()
        {
            Root = new SceneNode("Root");
        }
        public SceneNode Root;
    }
}
