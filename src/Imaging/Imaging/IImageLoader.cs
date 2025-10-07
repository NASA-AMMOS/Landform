namespace JPLOPS.Imaging
{
    public interface IImageLoader
    {
        Image LoadImage(string url, IImageConverter converter = null);
    }
}
