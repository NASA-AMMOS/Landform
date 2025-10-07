using JPLOPS.Geometry;
using JPLOPS.Pipeline.Texturing;

namespace JPLOPS.Pipeline
{
    public class TexturingDefaults
    {
        public const double RAYCAST_TOLERANCE = 0.001;
        public const double FRUSTUM_HULL_TEST_EPSILON = 0.00001;

        public const double TEXTURE_FAR_CLIP = 64;
        public const double EXTEND_SURFACE_EXTENT = 2;

        public const ObsSelectionStrategyName OBS_SEL_STRATEGY = ObsSelectionStrategyName.Spatial;
        public const double OBS_SEL_QUALITY = 1;
        public const int OBS_SEL_MAX_CONTEXTS = 32;
        public const double OBS_SEL_EQUIVALENT_SCORES_ABS = 0.001;
        public const double OBS_SEL_EQUIVALENT_SCORES_REL = 0.2;
        public const PreferColorMode OBS_SEL_PREFER_COLOR = PreferColorMode.EquivalentScores;
        public const bool OBS_SEL_PREFER_SURFACE = true;
        public const bool OBS_SEL_PREFER_NONLINEAR = true;
        public const double OBS_SEL_QUALITY_TO_SAMPLES_PER_SQUARE_METER = 100;
        public const double OBS_SEL_ORBITAL_QUALITY_TO_SAMPLES_PER_SQUARE_METER = 1;
        public const double OBS_SEL_SEARCH_RADIUS_SAMPLES = 2; //multipled by avg sample spacing to get search radius

        public const double BACKPROJECT_QUALITY = 0.3;
        public const double BACKPROJECT_MAX_GLANCING_ANGLE_DEGREES = 85;
        public const int BACKPROJECT_INPAINT_MISSING = 4;
        public const int BACKPROJECT_INPAINT_GUTTER = -1;
        public const int BACKPROJECT_MAX_SAMPLES_PER_BATCH = 500000;
        public static readonly float[] BACKPROJECT_NO_OBSERVATION_COLOR = new float[] { 0.5f, 0.5f, 0.5f };

        public const int SCENE_TEXTURE_RESOLUTION = 8192;
        public const double MIN_SURFACE_TEXTURE_FRACTION = 0.8;
        public const double EASE_TEXTURE_WARP = 0.5;
        public const double EASE_SURFACE_PPM_FACTOR = 0.2;
        public const AtlasMode ATLAS_MODE = AtlasMode.Manifold; //will fall back to UVAtlas and then HeightmapAtlas

        public const int BLEND_SHRINKWRAP_GRID_RESOLUTION = 1024;
        public const VertexProjection.ProjectionAxis BLEND_SHRINKWRAP_AXIS = VertexProjection.ProjectionAxis.Z;
        public const Shrinkwrap.ShrinkwrapMode BLEND_SHRINKWRAP_MODE = Shrinkwrap.ShrinkwrapMode.Project;
        public const Shrinkwrap.ProjectionMissResponse
            BLEND_SHRINKWRAP_MISS_RESPONSE = Shrinkwrap.ProjectionMissResponse.Delaunay;
        
        public const int OBSERVATION_BLUR_RADIUS = 7;
        public const int DIFF_BLUR_RADIUS = 7;
        public const int BLEND_TEXTURE_RESOLUTION = 4096;
        public const double BLEND_PREADJUST_LUMINANCE = 0.2;
        public const double SKY_PREADJUST_LUMINANCE = 0.2;
    }
}
