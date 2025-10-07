using JPLOPS.Util;

namespace JPLOPS.Imaging
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

        public override int GetHashCode()
        {
            return (Row < 65536 && Col < 65536) ? ((Row << 16) | Col) : HashCombiner.Combine(Row, Col);
        }

        public override bool Equals(object obj)
        {
            if (obj == null || !(obj is Pixel))
            {
                return false;
            }
            return Row == ((Pixel)obj).Row && Col == ((Pixel)obj).Col;
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
