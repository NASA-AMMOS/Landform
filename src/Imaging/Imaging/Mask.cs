using System;
using System.Collections.Generic;

namespace JPLOPS.Imaging
{
    /// <summary>
    /// Class to track masked out points in an image using either an array or a hashset.
    /// Using a hash is preferable if the image is large or the number of masked points is small.
    /// An array is preferable if the number of masked points is large.
    /// </summary>
    public class Mask
    {
        private bool useHash = true;
        private BinaryImage image;
        private HashSet<Tuple<int, int>> hash;

        public Mask(int width, int height, bool useHash)
        {
            this.useHash = useHash;
            if (useHash)
            {
                hash = new HashSet<Tuple<int, int>>();
            }
            else
            {
                image = new BinaryImage(width, height);
            }
        }

        public bool IsValid(int r, int c)
        {
            if (useHash)
            {
                return !hash.Contains(new Tuple<int, int>(r, c));
            }
            else
            {
                return !image[r, c];
            }
        }

        public void SetValid(int r, int c)
        {
            if (useHash)
            {
                hash.Remove(new Tuple<int, int>(r, c));
            }
            else
            {
                image[r, c] = false;
            }
        }

        public void SetInvalid(int r, int c)
        {
            if (useHash)
            {
                hash.Add(new Tuple<int, int>(r, c));
            }
            else
            {
                image[r, c] = true;
            }
        }
    }
}
