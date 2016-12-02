using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    /// <summary>
    /// Articulation parameters for a rover pose. All angles are in radians.
    /// </summary>
    public class RoverArticulation
    {
        public double LeftRockerAngle;
        public double LeftBogieAngle;
        public double RightBogieAngle;
        public double RightRockerAngle
        {
            get
            {
                return -LeftRockerAngle;
            }
        }
        public double ArmAngle1;
        public double ArmAngle2;
        public double ArmAngle3;
        public double ArmAngle4;
        public double ArmAngle5;

        public double MastAzimuth;
        public double MastElevation;
    }
}
