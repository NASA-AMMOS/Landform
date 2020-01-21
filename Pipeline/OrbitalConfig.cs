using OPS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public class OrbitalConfig : SingletonConfig<OrbitalConfig>
    {
        [ConfigEnvironmentVariable("LANDFORM_DEM_REL_PATH")]
        public string DEMRelPath { get; set; } = "orbital/orbital_dem.tif";
        public override string ConfigFileName() { return "orbital"; } //config file will be ~/.landform/orbital.json
    }
}
