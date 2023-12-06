using JPLOPS.Util;

namespace JPLOPS.Geometry
{
    public class MeshSerializers : SerializerMap<MeshSerializer>
    {
        //it is surprisingly hairy to "just" inherit from Singleton in this class hierarchy
        private static MeshSerializers instance;
        public static MeshSerializers Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new MeshSerializers();
                }
                return instance;
            }
        }

        protected override void RegisterSerializers()
        {
            new OBJSerializer().Register(this);
            new PLYSerializer().Register(this);
            new IVSerializer().Register(this);
            new GLTFSerializer().Register(this);
            new GLBSerializer().Register(this);
            new B3DMSerializer().Register(this);
            new BOBSerializer().Register(this);
            new STLSerializer().Register(this);
            new PNTSSerializer().Register(this);
        }

        public override string Kind()
        {
            return "mesh";
        }
    }
}
