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
    }
}
