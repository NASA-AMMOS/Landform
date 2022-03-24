using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using JPLOPS.Imaging;

namespace JPLOPS.Geometry
{

    public class PTXFileData
    {
        public Mesh Mesh { get; set; }
        public Matrix Transform { get; set; }
        public Image Image { get; set; }
        public Image XYZ { get; set; }
        public Image Scalar { get; set; }

        public Vector3 SensorPosition;
        public Vector3 SensorAxisX;
        public Vector3 SensorAxisY;
        public Vector3 SensorAxisZ;
        
        public PTXFileData() { }



        public PTXFileData(string filename, bool headerOnly = false)
        {
            using (var fs = File.OpenRead(filename))
            {
                ReadFromStream(fs, headerOnly);
            }
        }

        void ReadFromStream(FileStream stream, bool headerOnly)
        {
            using (var sr = new StreamReader(stream))
            {
                int numCol = int.Parse(sr.ReadLine());
                int numRow = int.Parse(sr.ReadLine());
                this.SensorPosition = Read3(sr);
                this.SensorAxisX = Read3(sr);
                this.SensorAxisY = Read3(sr);
                this.SensorAxisZ = Read3(sr);
                Vector4 t0 = Read4(sr);
                Vector4 t1 = Read4(sr);
                Vector4 t2 = Read4(sr);
                Vector4 t3 = Read4(sr);

                this.Transform = new Matrix(t0.X, t0.Y, t0.Z, t0.W,
                                            t1.X, t1.Y, t1.Z, t1.W,
                                            t2.X, t2.Y, t2.Z, t2.W,
                                            t3.X, t3.Y, t3.Z, t3.W);
                if(headerOnly)
                {
                    return;
                }

                this.XYZ = new Image(3, numCol, numRow);
                this.Image = new Image(3, numCol, numRow);
                this.Scalar = new Image(1, numCol, numRow);

                //ProgressReporter reporter = new ProgressReporter(10, x => Console.WriteLine(x + "%"));
                Console.WriteLine("Reading file");
                for (int c = 0; c < numCol; c++)
                {
                    //reporter.Update((int)((c / (float)numCol)*100));
                    for (int r = 0; r < numRow; r++)
                    {
                        var parts = sr.ReadLine().Split();
                        if (parts.Length != 7)
                        {
                            throw new Exception("Unknown point data");
                        }
                        var values = parts.Select(p => double.Parse(p)).ToArray();
                        var pos = new Vector3(values[0], values[1], values[2]);
                        this.XYZ[0, r, c] = (float)pos.R;
                        this.XYZ[1, r, c] = (float)pos.G;
                        this.XYZ[2, r, c] = (float)pos.B;

                        var scalar = values[3];
                        this.Scalar[0, r, c] = (float)scalar;

                        var color = new Vector4(values[4], values[5], values[6], 1) / 255;
                        this.Image[0, r, c] = (float)color.R;
                        this.Image[1, r, c] = (float)color.G;
                        this.Image[2, r, c] = (float)color.B;
                    }
                }
                Console.WriteLine("Generating mesh");
                this.Mesh = new Mesh(hasColors: true);
                for (int c = 0; c < numCol; c++)
                {
                    //reporter.Update((int)((c / (float)numCol) * 100));
                    for (int r = 0; r < numRow; r++)
                    {                        
                        var pos = new Vector3(this.XYZ[0, r, c], this.XYZ[1, r, c], this.XYZ[2, r, c]);
                        var color = new Vector4(this.Image[0, r, c], this.Image[1, r, c], this.Image[2, r, c], 1);
                        if(pos == Vector3.Zero)
                        {
                            continue;
                        }
                        this.Mesh.Vertices.Add(new Vertex(pos, Vector3.Zero, color, Vector2.Zero));
                    }
                }
            }
        }

        public void FilterMesh(double distance)
        {
            double distSqrd = distance * distance;
            List<Vertex> verts = new List<Vertex>();
            foreach(var v in this.Mesh.Vertices)
            {
                if((v.Position - this.SensorPosition).LengthSquared() <= distSqrd)
                {
                    verts.Add(v);
                }
            }
            this.Mesh.Vertices = verts;
        }

        Vector3 Read3(StreamReader sr)
        {
            var parts = sr.ReadLine().Split();
            if(parts.Length != 3)
            {
                throw new Exception("Unknown vector 3");
            }
            return new Vector3(double.Parse(parts[0]), double.Parse(parts[1]), double.Parse(parts[2]));
        }

        Vector4 Read4(StreamReader sr)
        {
            var parts = sr.ReadLine().Split();
            if (parts.Length != 4)
            {
                throw new Exception("Unknown vector 4");
            }
            return new Vector4(double.Parse(parts[0]), double.Parse(parts[1]), double.Parse(parts[2]), double.Parse(parts[3]));
        }

    }

    public class PTXSerializer : MeshSerializer
    {
        
        public override string GetExtension()
        {
            return ".ptx";
        }

        public override Mesh Load(string filename)
        {
            return new PTXFileData(filename).Mesh;
        }

        public override void Save(Mesh m, string filename, string imageFilename)
        {
            throw new System.NotImplementedException();
        }


        

    }
}
