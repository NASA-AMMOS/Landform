using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
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

    public class PretendTilingServerDatabase
    {

        public List<TilingInputRecord> InputTable = new List<TilingInputRecord>();
        public List<TilingChunkRecord> ChunkTable = new List<TilingChunkRecord>();

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
            File.WriteAllText(DatabaseFilename, JsonHelper.ToJson(this, true));
        }

        public void Clear()
        {
            InputTable.Clear();
            ChunkTable.Clear();
        }







    }
}
