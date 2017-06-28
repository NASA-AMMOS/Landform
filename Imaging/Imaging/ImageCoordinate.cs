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
        public int b;
        /// <summary>
        /// Row
        /// </summary>
        public int r;
        /// <summary>
        /// Column
        /// </summary>
        public int c;
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="b">Band</param>
        /// <param name="r">Row</param>
        /// <param name="c">Column</param>
        public ImageCoordinate(int b, int r, int c)
        {
            this.b = b;
            this.r = r;
            this.c = c;
        }
    }
}
