using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace OPS.Imaging
{
    /// <summary>
    /// Image converters are used to convert a GenericImage to Image or vice versa
    /// durring seralization.  Their primary responsibility is to convert between
    /// T and float and to apply appropriate scaling of values
    /// </summary>
    public interface IImageConverter<T>
    {
        Image FromGeneric(GenericImage<T> genericImage);
        GenericImage<T> ToGeneric(GenericImage<T> image);
    }
}
