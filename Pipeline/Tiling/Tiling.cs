using System;
using OPS.Geometry;

namespace OPS.Pipeline
{
    public enum TilingScheme
    {        
        Bin,
        QuadX,
        QuadY,
        QuadZ,
        Oct,
        UserDefined
    }

    public enum TextureMode
    {
        None,
        Clip,       //generate tile textures by clipping regions out of the source texture and offsetting uvs
        Bake,       //generate tile textures by atlassing tiles and sampling source texture at a desired resolution
        Backproject //generate tile textures by choosing the best data from observations that viewed the mesh
    }

    public static class TilingDefaults
    {
        public const TilingScheme TILING_SCHEME = TilingScheme.Bin;
        public const int MAX_FACES_PER_TILE = 2000;
        public const MeshReconstructionMethod PARENT_RECONSTRUCTION_METHOD = MeshReconstructionMethod.FSSR;
        public const SkirtMode SKIRT_MODE = SkirtMode.None;

        public const TextureMode TEXTURE_MODE = TextureMode.Bake;
        public const int MAX_TEXTURE_RESOLUTION = 512;
        public const int MIN_TEXTURE_RESOLUTION = 16;
        public const double MAX_TEXELS_PER_METER = 1024;
        public const double MAX_TEXTURE_STRETCH = 1;
        public const bool POWER_OF_TWO_TEXTURES = false; //changing to true requires refactoring comand line options

        public const string EXPORT_DIR = "www";
        public const string TILESET_DIR = "www";
        public const string INTERNAL_TILE_DIR = "tiles";

        public const string INTERNAL_MESH_FORMAT = "ply";
        public const string INTERNAL_IMAGE_FORMAT = "png";

        public const string TILESET_MESH_FORMAT = "b3dm";
        public const string TILESET_IMAGE_FORMAT = "jpg";

        public const int MAX_LEAF_GROUP = 32;

        public const double CHILD_BOUNDS_SEARCH_RATIO = 1.1f;

        public const int TEXTURE_PATCH_BORDER_SIZE = 5;
        public const bool TEXTURE_PATCH_ALLOW_ROTATION = false;

        public const double TEX_SPLIT_PERCENT_TO_TEST = 0.03;
        public const double TEX_SPLIT_PERCENT_SATISFIED = 0.5;
        public const double TEX_SPLIT_MAX_PIXELS_PER_TEXEL = 16;
    }
}
