using OPS.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    /// <summary>
    /// Interface for image correspondence filters.
    /// </summary>
    public interface IMatchFilter
    {
        /// <summary>
        /// Return a reduced subset of matches given an existing
        /// correspondence.
        /// </summary>
        /// <param name="matches">Set of matches to filter</param>
        /// <returns>Hopefully better set of matches</returns>
        ImagePairCorrespondence Filter(ImagePairCorrespondence matches);
    }

}
