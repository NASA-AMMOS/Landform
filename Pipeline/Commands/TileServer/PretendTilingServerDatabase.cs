using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using OPS.Util;

namespace OPS.Pipeline
{

    public class TilingInputRecord
    {
        public string MeshFilename;
        public string ImageFilename;

        public TilingInputRecord() { }

        public TilingInputRecord(string mesh, string image)
        {
            this.MeshFilename = mesh;
            this.ImageFilename = image;
        }
    }

    public class TilingChunkRecord
    {
        public string MeshFilename;
        public string ImageFilename;
        public BoundingBox Bounds;


        public TilingChunkRecord() { }

        public TilingChunkRecord(string mesh, string image, BoundingBox bounds)
        {
            this.MeshFilename = mesh;
            this.ImageFilename = image;
            this.Bounds = bounds;
        }
    }

    public class NodeRecord
    {
        public string Id;
        public string ParentId;
        public string[] ChildIds;
        public BoundingBox Bounds;
        public string MeshFilename;
        public string ImageFilename;
    } 

    public class PretendTilingServerDatabase
    {
        object lockObject = new object();

        public ConcurrentBag<TilingInputRecord> InputTable = new ConcurrentBag<TilingInputRecord>();
        public ConcurrentBag<TilingChunkRecord> ChunkTable = new ConcurrentBag<TilingChunkRecord>();
        public ConcurrentBag<NodeRecord> NodeTable = new ConcurrentBag<NodeRecord>();


        static PretendTilingServerDatabase instance;

        public static PretendTilingServerDatabase Instance
        {
            get
            {
                if(instance == null)
                {
                    if(File.Exists(DatabaseFilename))
                    {
                        instance = (PretendTilingServerDatabase) JsonHelper.FromJson(File.ReadAllText(DatabaseFilename));
                    }
                    else
                    {
                        instance = new PretendTilingServerDatabase();
                    }
                }
                return instance;
            }
        }

        public static string DatabaseFilename
        {
            get
            {
                return Path.GetFullPath("pretendTilingDatabase.json");
            }
        }

        public void Save()
        {
            lock (lockObject)
            {
                File.WriteAllText(DatabaseFilename, JsonHelper.ToJson(this, true));
            }
        }

        public void Clear()
        {
            lock (lockObject)
            {
                InputTable = new ConcurrentBag<TilingInputRecord>();
                ChunkTable = new ConcurrentBag<TilingChunkRecord>();
                NodeTable = new ConcurrentBag<NodeRecord>();
            }
        }
    }
}
