using OPS.Util;

namespace OPS.Imaging
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
            new PDSSeralizer().Register(this);
            new DDSSerializer().Register(this);
            new GDALSeralizer().Register(this);
            new FITSSerializer().Register(this);
            new RGBSerializer().Register(this);
        }
    }
}
