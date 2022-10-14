using JPLOPS.Util;

namespace JPLOPS.Imaging
{
    public class ImageSerializers : SerializerMap<ImageSerializer>
    {
        //it is surprisingly hairy to "just" inherit from Singleton in this class hierarchy
        private static ImageSerializers instance;
        public static ImageSerializers Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new ImageSerializers();
                }
                return instance;
            }
        }

        protected override void RegisterSerializers()
        {
            new PDSSerializer().Register(this);
            new GDALSerializer().Register(this);
            new FITSSerializer().Register(this);
            new RGBSerializer().Register(this);
            new PPMSerializer().Register(this);
            new ImageSharpSerializer().Register(this);
        }

        public override string Kind()
        {
            return "image";
        }
    }
}
