using Microsoft.Xna.Framework;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PairViewer
{
    public class ImageView
    {
        OPS.Imaging.Image image;
        Bitmap bitmap;
        public PictureBox Box;
        public ImageRef ImageRef;

        public Bitmap Overlay;

        public ImageView(PictureBox box)
        {
            this.Box = box;
        }

        public OPS.Imaging.Image Image
        {
            get
            {
                return image;
            }
            set
            {
                if (value != null)
                {
                    // create new bitmap
                    bitmap = new Bitmap(value.Width, value.Height);
                    for (int j = 0; j < value.Height; j++)
                    {
                        for (int i = 0; i < value.Width; i++)
                        {
                            var vals = value.GetBandValues(j, i);
                            if (vals.Length == 1)
                            {
                                vals = new[] { vals[0], vals[0], vals[0] };
                            }
                            byte r = (byte)(vals[0] * 255);
                            byte g = (byte)(vals[1] * 255);
                            byte b = (byte)(vals[2] * 255);
                            bitmap.SetPixel(i, j, Color.FromArgb(r, g, b));
                        }
                    }
                    Box.BackgroundImage = bitmap;

                    Overlay = new Bitmap(value.Width, value.Height);
                    Box.Image = Overlay;
                }
                else
                {
                    Box.BackgroundImage = null;
                    Box.Image = null;
                }

                image = value;
            }
        }

        public Vector2 ControlToPixel(PointF pt)
        {
            // stolen from https://bytes.com/topic/net/answers/645830-getting-x-y-coordinates-picturebox-image
            int imgWidth = Box.Image.Width;
            int imgHeight = Box.Image.Height;
            int boxWidth = Box.Size.Width;
            int boxHeight = Box.Size.Height;

            //This variable will hold the result
            float X = pt.X;
            float Y = pt.Y;
            //Comparing the aspect ratio of both the control and the image itself.
            if (imgWidth / imgHeight > boxWidth / boxHeight)
            {
                //If true, that means that the image is stretched through the width of the control.
                //'In other words: the image is limited by the width.

                //The scale of the image in the Picture Box.
                float scale = boxWidth / (float)imgWidth;

                //Since the image is in the middle, this code is used to determinate the empty space in the height
                //'by getting the difference between the box height and the image actual displayed height and dividing it by 2.
                float blankPart = (boxHeight - scale * imgHeight) / 2;

                Y -= blankPart;

                //Scaling the results.
                X /= scale;
                Y /= scale;
            }
            else
            {
                //If true, that means that the image is stretched through the height of the control.
                //'In other words: the image is limited by the height.

                //The scale of the image in the Picture Box.
                float scale = boxHeight / (float)imgHeight;

                //Since the image is in the middle, this code is used to determinate the empty space in the width
                //'by getting the difference between the box width and the image actual displayed width and dividing it by 2.
                float blankPart = (boxWidth - scale * imgWidth) / 2;
                X -= blankPart;

                //Scaling the results.
                X /= scale;
                Y /= scale;
            }

            return new Vector2(X, Y);
        }
    }

}
