using System;
using System.Collections;
using System.Collections.Generic;

namespace OPS.Pipeline
{
    public class ContextualMeshParameters
    {
        public string RDRDir;
        public int PrimarySol;
        public HashSet<int> Sols = new HashSet<int>();
        public SiteDrive PrimarySiteDrive;
        public HashSet<SiteDrive> SiteDrives = new HashSet<SiteDrive>();
        public string TilesetName { get { return string.Format("{0:D4}_{1}", PrimarySol, PrimarySiteDrive); } }
    }
}
