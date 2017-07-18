using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{
    /// <summary>
    /// Implementation of 3D Tile spec
    /// https://github.com/AnalyticalGraphicsInc/3d-tiles/blob/master/schema/
    /// </summary>
    public class Tile3D
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public enum RefineMode
        {
            add,
            replace
        }

        /// <summary>
        /// Metadata about the entire tileset
        /// </summary>
        public class Asset
        {
            /// <summary>
            /// required
            /// The 3D Tiles version.  The version defines the JSON schema for tileset.json and the base set of tile formats.
            /// </summary>
            public string version = "0.0";

            /// <summary>
            /// optional
            /// Application-specific version of this tileset, e.g., for when an existing tileset is updated
            /// </summary>
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string tilesetVersion = null;

            public bool IsValid()
            {
                return version != null;
            }
        }

        /// <summary>
        /// A dictionary object of metadata about per-feature properties
        /// </summary>
        public class Property
        {
            /// <summary>
            /// required
            /// The maximum value of this property of all the features in the tileset
            /// </summary>
            public double minimum;

            /// <summary>
            /// required
            /// The minimum value of this property of all the features in the tileset
            /// </summary>
            public double maximum;
        }

        /// <summary>
        /// Metadata about the tile's content and a link to the content
        /// </summary>
        public class Content
        {
            /// <summary>
            /// required
            /// An optional bounding volume that tightly enclosing just the tile's contents. This is used for replacement refinement; tile.boundingVolume provides spatial coherence and tile.content.boundingVolume enables tight view frustum culling. When this is omitted, tile.boundingVolume is used.
            /// </summary>
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public BoundingVolume boundingVolume;

            /// <summary>
            /// optional 
            /// A string that points to the tile's contents with an absolute or relative url. When the url is relative, it is relative to the referring tileset.json. The file extension of content.url defines the tile format. The core 3D Tiles spec supports the following tile formats: Batched 3D Model (*.b3dm), Instanced 3D Model (*.i3dm), Composite (*.cmpt), and 3D Tiles TileSet (*.json)
            /// gltf model spec specifies a right-handed coordinate system and defines the y axis as up
            /// </summary>
            public string url;

            /// <summary>
            /// unoffical addition to support non standard data formats
            /// </summary>
            public string textureExension;

            public Content()
            {
            }

            public bool IsValid()
            {
                if (url == null)
                {
                    return false;
                }
                if (boundingVolume != null && !boundingVolume.IsValid())
                {
                    return false;
                }
                return true;
            }

        }

        /// <summary>
        /// A bounding volume that encloses a tile or its contents.  At least one property is required.  If more than one property is defined, the runtime can determine which to use
        /// required that at least one of these properties is not null
        /// </summary>
        public class BoundingVolume
        {

            /// <summary>
            /// An array of 12 numbers that define an oriented bounding box in WGS84 coordinates.  The first three elements define the x, y, and z values for the center of the box.  The next three elements (with indices 3, 4, and 5) define the x axis direction and length.  The next three elements (indices 6, 7, and 8) define the y axis direction and length.  The last three elements (indices 9, 10, and 11) define the z axis direction and length
            /// </summary>
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public double[] box;

            /// <summary>
            /// An array of six numbers that define a bounding geographic region with the order [west, south, east, north, minimum height, maximum height]. Longitudes and latitudes are in radians, and heights are in meters above (or below) the WGS84 ellipsoid
            /// </summary>
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public double[] region;

            /// <summary>
            /// An array of four numbers that define a bounding sphere.  The first three elements define the x, y, and z values for the center of the sphere.  The last element (with index 3) defines the radius in meters.
            /// </summary>
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public double[] sphere;

            public bool IsBox()
            {
                return box != null;
            }

            /// <summary>
            /// Returns an axis aligned bounding box
            /// This may not correspond exactly to the bounding box defined by box
            /// </summary>
            /// <returns></returns>
            [JsonIgnore]
            public BoundingBox? Bounds
            {
                get
                {
                    if (!IsBox())
                    {
                        return null;
                    }
                    Vector3 center, xaxis, yaxis, zaxis;
                    center.X = box[0];
                    center.Y = box[1];
                    center.Z = box[2];
                    xaxis.X = box[3];
                    xaxis.Y = box[4];
                    xaxis.Z = box[5];
                    yaxis.X = box[6];
                    yaxis.Y = box[7];
                    yaxis.Z = box[8];
                    zaxis.X = box[9];
                    zaxis.Y = box[10];
                    zaxis.Z = box[11];

                    Vector3 extent;
                    extent.X = Math.Max(Math.Max(xaxis.X, yaxis.X), zaxis.X);
                    extent.Y = Math.Max(Math.Max(xaxis.Y, yaxis.Y), zaxis.Y);
                    extent.Z = Math.Max(Math.Max(xaxis.Z, yaxis.Z), zaxis.Z);

                    Vector3 halfExtent = extent / 2;

                    Vector3 min = center - halfExtent;
                    Vector3 max = center + halfExtent;
                    return new BoundingBox(min, max);
                }
                set
                {
                    if (value == null)
                    {
                        box = null;
                    }
                    else
                    {
                        BoundingBox b = (BoundingBox)value;
                        Vector3 extent = b.Max - b.Min;
                        Vector3 center = (b.Max + b.Min) / 2;
                        Vector3 xaxis = new Vector3(extent.X, 0, 0);
                        Vector3 yaxis = new Vector3(0, extent.Y, 0);
                        Vector3 zaxis = new Vector3(0, 0, extent.Z);
                        box = new double[] { center.X, center.Y, center.Z, xaxis.X, xaxis.Y, xaxis.Z, yaxis.X, yaxis.Y, yaxis.Z, zaxis.X, zaxis.Y, zaxis.Z };
                    }
                }
            }

            public bool IsValid()
            {
                if (box == null && region == null && sphere == null)
                {
                    return false;
                }
                if (box != null && box.Length != 12)
                {
                    return false;
                }
                if (region != null && region.Length != 6)
                {
                    return false;
                }
                if (sphere != null && sphere.Length != 4)
                {
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// A tile in a 3D Tiles tileset
        /// 
        /// </summary>
        public class Tile
        {
            /// <summary>
            /// required
            /// The bounding volume that encloses the tile
            /// </summary>
            public BoundingVolume boundingVolume;

            /// <summary>
            /// optional
            /// defines a volume that the viewer must be inside of before the tile's content will be requested and before the tile will be refined based on geometricError
            /// </summary>
            public BoundingVolume viewerRequestVolume;

            /// <summary>
            /// required
            /// The error, in meters, introduced if this tile is rendered and its children are not. At runtime, the geometric error is used to compute Screen-Space Error (SSE), i.e., the error measured in pixels
            /// </summary>
            public double geometricError;

            /// <summary>
            /// required for root tile
            /// Specifies if additive or replacement refinement is used when traversing the tileset for rendering.  This property is required for the root tile of a tileset; it is optional for all other tiles.  The default is to inherit from the parent tile
            /// </summary>
            public RefineMode? refine;

            /// <summary>
            /// optional
            /// Metadata about the tile's content and a link to the content. When this is omitted the tile is just used for culling. This is required for leaf tiles
            /// </summary>
            public Content content;


            /// <summary>
            /// optional
            /// The transform property is a 4x4 affine transformation matrix, stored in column-major order, that transforms from the tile's local coordinate system to the parent tile's coordinate system, or tileset's coordinate system in the case of the root tile.
            /// </summary>
            public double[] transform;

            /// <summary>
            /// Helper method to convert raw transform values to and from Matrix class
            /// Note that Matrix class row-major and transform array is column major.
            /// </summary>
            [JsonIgnore]
            public Matrix TransformMatrix
            {
                get
                {
                    if(transform == null)
                    {
                        return Matrix.Identity;
                    }
                    // Create matrix from raw elements in column-major order
                    Matrix m = new Matrix();
                    m.M11 = transform[0];
                    m.M21 = transform[1];
                    m.M31 = transform[2];
                    m.M41 = transform[3];

                    m.M12 = transform[4];
                    m.M22 = transform[5];
                    m.M32 = transform[6];
                    m.M42 = transform[7];

                    m.M13 = transform[8];
                    m.M23 = transform[9];
                    m.M33 = transform[10];
                    m.M43 = transform[11];

                    m.M14 = transform[12];
                    m.M24 = transform[13];
                    m.M34 = transform[14];
                    m.M44 = transform[15];

                    return m;
                }
                set
                {
                    transform = new double[16];
                    transform[0] = value.M11;
                    transform[1] = value.M21;
                    transform[2] = value.M31;
                    transform[3] = value.M41;

                    transform[4] = value.M12;
                    transform[5] = value.M22;
                    transform[6] = value.M32;
                    transform[7] = value.M42;

                    transform[8] = value.M13;
                    transform[9] = value.M23;
                    transform[10] = value.M33;
                    transform[11] = value.M43;

                    transform[12] = value.M14;
                    transform[13] = value.M24;
                    transform[14] = value.M34;
                    transform[15] = value.M44;
                }
            }

            /// <summary>
            /// optional defaults to []
            /// An array of objects that define child tiles. Each child tile has a box fully enclosed by its parent tile's box and, generally, a geometricError less than its parent tile's geometricError. For leaf tiles, the length of this array is zero, and children may not be defined
            /// </summary>
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public List<Tile> children;


            public Tile()
            {
                boundingVolume = new BoundingVolume();
                children = new List<Tile>();
            }

            public Tile(BoundingBox bounds, double geometricError = 0)
            {
                boundingVolume = new BoundingVolume();
                this.geometricError = geometricError;
                children = new List<Tile>();
                this.boundingVolume.Bounds = bounds;
            }

            public bool IsValid()
            {
                if (boundingVolume == null || !boundingVolume.IsValid())
                {
                    return false;
                }
                foreach (var c in children)
                {
                    if (!c.IsValid())
                    {
                        return false;
                    }
                }
                return true;
            }

        }

        /// <summary>
        /// A 3D Tiles tileset
        /// </summary>
        public class Tileset
        {

            /// <summary>
            /// required
            /// Metadata about the entire tileset
            /// </summary>
            public Asset asset;

            //// <summary>
            /// required
            /// A dictionary object of metadata about per-feature properties
            /// </summary>                     
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public Dictionary<string, Property> properties;

            /// <summary>
            /// required
            /// The error, in meters, introduced if this tileset is not rendered. At runtime, the geometric error is used to compute Screen-Space Error (SSE), i.e., the error measured in pixels
            /// </summary>
            public double geometricError;

            /// <summary>
            /// required
            /// The root node
            /// </summary>
            public Tile root;

            public Tileset()
            {
                asset = new Asset();
                properties = new Dictionary<string, Property>();
            }

            public bool IsValid()
            {
                if (asset == null || !asset.IsValid())
                {
                    return false;
                }
                if (properties == null)
                {
                    return false;
                }
                if (root == null || root.refine == null)
                {
                    return false;
                }
                if (!root.IsValid())
                {
                    return false;
                }
                return true;
            }
        }
    }
}
