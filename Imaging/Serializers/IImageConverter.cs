namespace JPLOPS.Imaging
{
    /// <summary>
    /// Image converters are used when reading or writing images from files
    /// The goal is to convert from the files underlying datatype to the
    /// normalized representation expected by the Image class.
    /// </summary>
    public interface IImageConverter
    {
        /// <summary>
        /// Converts image values between normalized and raw form
        /// For example, convert could go from byte RGB values 0-255 to
        /// normalized float 0-1.  Or it could do the opposite.
        /// </summary>
        /// <param name="image"></param>
        /// <returns></returns>
        Image Convert<T>(Image image);
    }

    
}
