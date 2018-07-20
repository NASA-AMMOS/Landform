using Amazon.DynamoDBv2.DataModel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Cloud
{
    public class FrameCache
    {
        ConcurrentDictionary<string, Frame> frames = new ConcurrentDictionary<string, Frame>();

        DynamoDBContext dynamoContext;
        string projectName;

        public FrameCache(DynamoDBContext dynamoContext, string projectName)
        {
            this.dynamoContext = dynamoContext;
            this.projectName = projectName;
        }

        public Frame GetFrame(string name)
        {
            if (!frames.ContainsKey(name))
            {
                var frame = ThroughputManager.Run(() => Frame.Find(dynamoContext, projectName, name));
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
