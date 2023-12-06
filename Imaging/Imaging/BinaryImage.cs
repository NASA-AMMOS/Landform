using System;

namespace JPLOPS.Imaging
{
    public class BinaryImage
    {
        public int Width { get; protected set; }
        public int Height { get; protected set; }

        protected bool[,] data;

        protected BinaryImage() { }

        public BinaryImage(int width, int height)
        {
            this.Width = width;
            this.Height = height;
            data = new bool[height, width];
        }

        public virtual bool this[int row, int column]
        {
            get
            {
                return data[row, column];
            }

            set
            {
                data[row, column] = value;
            }
        }

        public BinaryImage DilateErode(int dilatePixels = 1, int erodePixels = 1, BinaryImage work = null)
        {
            if (work == null)
            {
                work = new BinaryImage(Width, Height);
            }
            else if (work.Width != Width || work.Height != Height)
            {
                throw new ArgumentException("work image dimensions invalid");
            }
            bool open = dilatePixels < 0 && erodePixels < 0;
            if (open)
            {
                dilatePixels *= -1;
                erodePixels *= -1;
            }
            if (!open && (dilatePixels < 0 || erodePixels < 0))
            {
                throw new ArgumentException("dilate and erode must either be both negative or both non-negative");
            }
            BinaryImage src = this;
            BinaryImage dst = work;
            for (int k = 0; k < dilatePixels + erodePixels; k++)
            {
                for (int i = 0; i < Height; i++)
                {
                    for (int j = 0; j < Width; j++)
                    {
                        int s = 0;
                        for (int di = -1; di <= 1; di++)
                        {
                            for (int dj = -1; dj <= 1; dj++)
                            {
                                if (src[Math.Max(0, Math.Min(Height - 1, i + di)),
                                        Math.Max(0, Math.Min(Width - 1, j + dj))])
                                {
                                    s++;
                                }
                            }
                        }
                        if (open)
                        {
                            if (k < erodePixels)
                            {
                                dst[i, j] = s == 9; //erode
                            }
                            else
                            {
                                dst[i, j] = s > 0; //dilate
                            }
                        }
                        else
                        {
                            if (k < dilatePixels)
                            { 
                                dst[i, j] = s > 0; //dilate
                            }
                            else
                            {
                                dst[i, j] = s == 9; //erode
                            }
                        }
                    }
                }
                BinaryImage tmp = dst;
                dst = src;
                src = tmp;
            }
            return src;
        }

        public BinaryImage Dilate(int pixels = 1, BinaryImage work = null)
        {
            return DilateErode(pixels, 0, work);
        }

        public BinaryImage Erode(int pixels = 1, BinaryImage work = null)
        {
            return DilateErode(0, pixels, work);
        }

        public BinaryImage MorphologicalClose(int pixels = 1, BinaryImage work = null)
        {
            return DilateErode(pixels, pixels, work);
        }

        public BinaryImage MorphologicalOpen(int pixels = 1, BinaryImage work = null)
        {
            return DilateErode(-pixels, -pixels, work);
        }
    }
}
