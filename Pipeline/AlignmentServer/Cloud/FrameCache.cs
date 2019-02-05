using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;

namespace OPS.Pipeline.AlignmentServer
{
    /// <summary>
    /// Caches frames keyed on name upon load from dynamodb if enabled (never deletes frames)
    /// </summary>
    public class FrameCache
    {
        ConcurrentDictionary<string, Frame> frames = new ConcurrentDictionary<string, Frame>();

        PipelineCore pipeline;
        string projectName;

        public FrameCache(PipelineCore pipeline, string projectName)
        {
            this.pipeline = pipeline;
            this.projectName = projectName;
        }

        public Frame GetFrame(string name)
        {
            if (!frames.ContainsKey(name))
            {
                Frame frame = Frame.Find(pipeline, projectName, name);
                if (frame == null)
                {
                    return null;
                }
                frames.TryAdd(name, frame);
            }
            return frames[name];
        }
    }
}
