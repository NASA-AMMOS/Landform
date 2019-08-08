namespace OPS.Geometry
{
    public class ThirdPartyMeshSerializers
    {
        public static void Register()
        {
            new DAESerializer().Register();
            new DracoSerializer().Register();
            new OpenInventorSerializer().Register();
            //new OPS.Geometry.Experimental.AssimpSerializer().Register();
        }
    }
}
