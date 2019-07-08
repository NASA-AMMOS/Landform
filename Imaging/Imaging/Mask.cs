using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    /// <summary>
    /// Class to track masked out points in an image using either an array or a hashset.
    /// Using a hash is preferable if the image is large or the number of masked points is small.
    /// An array is preferable if the number of masked points is large.
    /// </summary>
    public class Mask
    {
        bool useHash = true;
        Image image;
        HashSet<Tuple<int, int>> hash;

        public Mask(int width, int height, bool useHash)
        {
            this.useHash = useHash;
            if (useHash)
            {
                hash = new HashSet<Tuple<int, int>>();
            }
            else
            {
                image = new Image(1, width, height);
                for (int r = 0; r < height; r++)
                {
                    for (int c = 0; c < width; c++)
                    {
                        image[0, r, c] = 1;
                    }
                }
            }
        }

        public bool isValid(int r, int c)
        {
            if (useHash)
            {
                return !hash.Contains(new Tuple<int, int>(r, c));
            }
            else
            {
                return image[0, r, c] != 0;
            }
        }

        public void setValid(int r, int c)
        {
            if (useHash)
            {
                if (hash.Contains(new Tuple<int, int>(r, c)))
                {
                    hash.Remove(new Tuple<int, int>(r, c));
                }
            }
            else
            {
                image[0, r, c] = 1;
            }
        }

        public void setInvalid(int r, int c)
        {
            if (useHash)
            {
                hash.Add(new Tuple<int, int>(r, c));
            }
            else
            {
                image[0, r, c] = 0;
            }
        }
    }
}
