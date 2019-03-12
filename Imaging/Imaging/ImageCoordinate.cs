using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    public struct Pixel
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

    public struct ImageCoordinate
    {
        public int Band;
        public int Row, Col;

        public ImageCoordinate(int band, int row, int col)
        {
            this.Band = band;
            this.Row = row;
            this.Col = col;
        }
    }
}
