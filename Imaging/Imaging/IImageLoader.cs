namespace OPS.Imaging
{
    public interface IImageLoader
    {
        Image LoadImage(string url, IImageConverter converter = null);
    }
}
