using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry.GLTF
{        
    /// <summary>
    /// Structure classes for serializing gltf files as json
    /// </summary>
    public class GLTFFile
    {
        public int scene;
        public GLTFAsset asset = new GLTFAsset();
        public List<GLTFAccessor> accessors = new List<GLTFAccessor>();
        public List<GLTFNode> nodes = new List<GLTFNode>();
        public List<GLTFScene> scenes = new List<GLTFScene>();
        public List<GLTFBuffer> buffers = new List<GLTFBuffer>();
        public List<GLTFBufferView> bufferViews = new List<GLTFBufferView>();
        public List<GLTFMesh> meshes = new List<GLTFMesh>();
        public List<GLTFImage> images = new List<GLTFImage>();
        public List<GLTFSampler> samplers = new List<GLTFSampler>();
        public List<GLTFTexture> textures = new List<GLTFTexture>();
        public List<GLTFMaterial> materials = new List<GLTFMaterial>();
    }

    public class GLTFAsset
    {
        public string generator = "landform";
        public string version = "2.0";
        public bool premultipliedAlpha = true;
    }

    public class GLTFNode
    {
        public int mesh;
    }

    public class GLTFScene
    {
        public string name;
        public List<int> nodes = new List<int>();
    }

    public class GLTFAccessor
    {
        public const int FLOAT_COMPONENT = 5126;
        public const int USHORT_COMPONENT = 5123;
        public const string VEC3_TYPE = "VEC3";
        public const string VEC2_TYPE = "VEC2";
        public const string SCALAR_TYPE = "SCALAR";
        
        public int bufferView;
        public int byteOffset;
        public int componentType;
        public int count;
        public float[] min;
        public float[] max;
        public string type;
        public string name;
    }

    public class GLTFBuffer
    {
        public const string OCTET_HEADER = "data:application/octet-stream;base64,";
        public int byteLength;
        public string uri;
    }

    public class GLTFBufferView
    {
        public int buffer;
        public int byteLength;
        public int byteOffset;
        public int byteStride;
    }

    public class GLTFMesh
    {
        public List<GLTFPrimitive> primitives = new List<GLTFPrimitive>();
    }

    public class GLTFPrimitive
    {
        public const int POINTS = 0;
        public const int TRIANGLES = 4;

        public Dictionary<string, int> attributes = new Dictionary<string, int>();
        public int? indices = null;
        public int? material = null;
        public int mode = TRIANGLES;
    }

    public class GLTFImage
    {
        public const string JPG_HEADER = "data:image/jpeg;base64,";
        public const string PNG_HEADER = "data:image/png;base64,";
        public string uri;
    }

    public class GLTFSampler
    {
        public const int LINEAR = 9729;
        public const int CLAMP = 33071;
        public const int REPEAT = 10497;

        public int magFilter = LINEAR;
        public int minFilter = LINEAR;
        public int wrapS = CLAMP;
        public int wrapT = CLAMP;
    }

    public class GLTFTexture
    {
        public int sampler;
        public int source;
    }

    public class GLTFMaterial
    {
        public GLTFPBRMetallicRoughness pbrMetallicRoughness = new GLTFPBRMetallicRoughness();
    }

    public class GLTFPBRMetallicRoughness
    {
        public float[] baseColorFactor = new float[] { 1, 1, 1, 1 };
        public GLTFTextureIndex baseColorTexture = new GLTFTextureIndex();
    }

    public class GLTFTextureIndex
    {
        public int index = 0;
    }
}
