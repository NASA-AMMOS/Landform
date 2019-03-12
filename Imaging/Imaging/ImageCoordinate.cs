using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    public class Pixel
    {
        public int Row, Col;
        
        public Pixel(int row, int col)
        {
            this.Row = row;
            this.Col = col;
        }
        
        public static Pixel operator+(Pixel a, Pixel b)
        {
            return new Pixel(a.Row + b.Row, a.Col + b.Col);
        }
    }

    public class ImageCoordinate : Pixel
    {
        public int Band;

        public ImageCoordinate(int b, int r, int c) : base(r, c)
        {
            this.Band = b;
        }
    }
}
