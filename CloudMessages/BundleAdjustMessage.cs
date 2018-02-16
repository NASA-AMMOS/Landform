using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Cloud
{
    [Message("BUNDLE_ADJUST")]
    public class BundleAdjustMessage : PipelineMessage
    {
        [Field("RootFrame")]
        public string RootFrame { get; set; }

        [Body]
        public List<string> AdjustedFrames { get; set; }

        public BundleAdjustMessage() { }
        public BundleAdjustMessage(string rootFrame, IEnumerable<string> adjustedFrames)
        {
            RootFrame = rootFrame;
            AdjustedFrames = adjustedFrames.ToList();
        }
    }
}
