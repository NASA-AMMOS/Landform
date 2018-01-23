using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public interface IOverlapDetector
    {
        void Detect(AlignmentScene scene);
    }
}
