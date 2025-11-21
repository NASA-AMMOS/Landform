
namespace JPLOPS.ImageFeatures
{
    class PCAConstants
    {
        public const int N = 36;
        const int SHORT_PATCH_SIZE = 39;
        public const int PATCH_LEN = SHORT_PATCH_SIZE * SHORT_PATCH_SIZE * 2;
        public const int PATCH_MAG = 20;
        public const int PATCH_SIZE = 41;
        public const double INIT_SIGMA = 0.5;
        public const float SIGMA = 1.6F;
        public const int SCALES_PER_OCTAVE = 3;
        public const int MAX_OCTAVES = 14;
        public const int GPLEN = (PATCH_SIZE - 2) * (PATCH_SIZE - 2) * 2;
        public const int PCALEN = 36;
        public const int EPCALEN = 36;
    }
}
