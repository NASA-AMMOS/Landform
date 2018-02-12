using OPS.Alignment;
using OPS.Cloud;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public class ComputedCorrespondence : JsonDataProduct
    {
        public Guid ModelFeaturesGuid;
        public Guid DataFeaturesGuid;
        public ImagePairCorrespondence Correspondence;
    }
}
