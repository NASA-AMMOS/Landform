using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    /// <summary>
    /// Represents a coordinate in an image
    /// </summary>
    public struct ImageCoordinate
    {
        /// <summary>
        /// Band
        /// </summary>
        public int Band;
        /// <summary>
        /// Row
        /// </summary>
        public int Row;
        /// <summary>
        /// Column
        /// </summary>
        public int Col;
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="b">Band</param>
        /// <param name="r">Row</param>
        /// <param name="c">Column</param>
        public ImageCoordinate(int b, int r, int c)
        {
            this.Band = b;
            this.Row = r;
            this.Col = c;
        }
    }
}
