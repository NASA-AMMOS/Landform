using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{

    /// <summary>
    /// Image type converters that simply cast to and from float without any scalinging
    /// </summary>
    public class NaiveImageConverter 
    {

        public class NaiveConverter<T> : IImageConverter<T>
        {
            public Image FromGeneric(GenericImage<T> genericImage)
            {
                throw new NotImplementedException();
            }

            public GenericImage<T> ToGeneric(GenericImage<T> image)
            {
                throw new NotImplementedException();
            }
        }

        public static NaiveConverter<byte> ByteConverter = new NaiveConverter<byte>();


      

    }
}
