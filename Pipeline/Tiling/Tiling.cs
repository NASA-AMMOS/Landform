using JPLOPS.Geometry;

namespace JPLOPS.Pipeline
{
    public enum TextureMode
    {
        None,
        Clip,       //generate tile textures by clipping regions out of the source texture and offsetting uvs
        Bake,       //generate tile textures by atlassing tiles and sampling source texture at a desired resolution
        Backproject //generate tile textures by choosing the best data from observations that viewed the mesh
    }

    public static class TilingDefaults
    {
        public const TilingScheme TILING_SCHEME = TilingScheme.QuadAuto;
        public const double PROGRESSIVE_TILING_FACTOR = 0.5;
        public const double MIN_TILE_EXTENT = 0.5;
        public const double MAX_TILE_EXTENT = -1;
        public const double MIN_TILE_EXTENT_REL = 0.1;
        public const double MAX_LEAF_AREA = 128;
        public const MeshReconstructionMethod PARENT_RECONSTRUCTION_METHOD = MeshReconstructionMethod.FSSR;
        public const SkirtMode SKIRT_MODE = SkirtMode.Z;

        //UVAtlas may crap out for some 16k tri meshes...
        //but 8192 is not quite enough to avoid splitting some 64x64 orbital tiles
        public const int MAX_FACES_PER_TILE = 10000;

        //8192tri*(1quad/2tri)=4096quad, sqrt(4096)=64quad*(1m/quad)=64m,  64*64=4096m^2 < 5km^2
        //512px*(1m/4px)=128m, 128*128=16384m^2
        public const double MAX_ORBITAL_LEAF_AREA = 5000;

        public const AtlasMode ATLAS_MODE = AtlasMode.Manifold; //will fall back to UVAtlas and then Heightmap
        public const int MAX_UVATLAS_SEC = 2 * 60;
        public const TextureMode TEXTURE_MODE = TextureMode.Bake;

        //these determine the range of intended maximum tile resolutions
        //computed based on tile mesh area and intended max texels per meter
        //actual tiles may have lower resolutions than either of these depending on other settings
        //(e.g. non-power of two, clipped textures)
        public const int MAX_TILE_RESOLUTION = 512;
        public const int MIN_TILE_RESOLUTION = 128;

        public const double MAX_TEXELS_PER_METER = 512;
        public const double MAX_ORBITAL_TEXELS_PER_METER = 4;
        public const bool TEXTURE_SPLIT_RESPECT_MAX_TEXELS_PER_METER = true; //requires refactoring command line options

        public const int MAX_TEXTURE_CHARTS = UVAtlas.DEF_MAX_CHARTS; //0 = unlimited
        public const double MAX_TEXTURE_STRETCH = UVAtlas.DEF_MAX_STRETCH; //0 = none, 1 = unlimited

        public const bool POWER_OF_TWO_TEXTURES = false; //requires refactoring comand line options

        public const string EXPORT_DIR = "www";
        public const string TILESET_DIR = "www";
        public const string INTERNAL_TILE_DIR = "tiles";

        public const string INTERNAL_MESH_FORMAT = "ply";
        public const string INTERNAL_IMAGE_FORMAT = "png";
        public const string INTERNAL_INDEX_FORMAT = "tif";

        public const string TILESET_MESH_FORMAT = "b3dm";
        public const string TILESET_IMAGE_FORMAT = "png";
        public const string TILESET_INDEX_FORMAT = "png";

        public const string INDEX_FILE_SUFFIX = "_index";
        public const string INDEX_FILE_EXT = ".tif";

        public const bool EMBED_INDEX_IMAGES = false; //requires refactoring command line options

        public const int MAX_LEAF_GROUP = 32;

        public const double CHILD_BOUNDS_SEARCH_RATIO = 1.1;

        public const int TEXTURE_PATCH_BORDER_SIZE = 5;
        public const bool TEXTURE_PATCH_ALLOW_ROTATION = false;

        public const double TEX_SPLIT_PERCENT_TO_TEST = 0.03;
        public const double TEX_SPLIT_PERCENT_SATISFIED = 0.5;
        public const double TEX_SPLIT_MAX_PIXELS_PER_TEXEL = 16;

        public const double PARENT_DECIMATE_BOUNDS_RATIO = 1.5;
        public const double PARENT_CLIP_BOUNDS_EXPAND_HEIGHT = 0.1;
        public const double PARENT_FACE_COUNT_RATIO = PARENT_DECIMATE_BOUNDS_RATIO * 1.1;
        public const double PARENT_SAMPLES_PER_FACE = 1.5; //tuned to try to avoid edge collapse in ResampleDecimation()
        public const double PARENT_HAUSDORFF_RELATIVE_ACCURACY = 0.005; //0.5% of mesh bounds
        public const double PARENT_MESH_VERTEX_MERGE_EPSILON = 0.002;

        public const double TEXTURE_ERROR_MULTIPLIER = 4;

        public const double MESH_HULL_TEST_EPSILON = 0.00001;
        public const double APPROX_TEXTURE_UTILIZATION = 0.5;
    }
}
