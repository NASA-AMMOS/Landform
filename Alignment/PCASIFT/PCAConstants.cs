
namespace OPS.Alignment
{
    class PCAConstants
    {
        public static int n = 36;
        public static int patchsize = 39;
        public static int patchlen =  patchsize * patchsize * 2;
        public static double PI = 3.14159256358979323846;
        public static int PATCHMAG = 20;
        public static int PATCHSIZE = 41;
        public static double INIT_SIGMA = 0.5;
        public static float SIGMA = 1.6F;
        public static int SCALES_PER_OCTAVE = 3;
        public static int MAX_OCTAVES = 14;
        public static int GPLEN = (PATCHSIZE - 2) * (PATCHSIZE - 2) * 2;
        public static int PCALEN = 36;
        public static int EPCALEN = 36;
    }
}
