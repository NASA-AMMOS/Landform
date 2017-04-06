using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using OPS.Cloud;
using OPS.Pipeline;
using System.Diagnostics;

namespace Landform
{
    class Landform
    {
        /// <summary>
        /// 
        /// The start of everything
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static int Main(string[] args)
        {
            /*
            var sh = new StorageHelper("menzies_m2020_gov", true);

            int i = 0;
            Stopwatch timer = new Stopwatch();
            timer.Start();
            //Parallel.ForEach(sh.SearchObjects("s3://m20-ocs-dev/ods/surface/sol/00583/opgs/rdr/ncam", "*.IMG", true), s =>
            foreach (var s in sh.SearchObjects("s3://m20-ocs-dev/ods/surface/sol/00583/opgs/rdr/ncam", "*.IMG", true))
            {
                
                //Console.WriteLine(s);
                sh.GetStorageStream(s, stream =>
                {
                    //using (var sr = new StreamReader(stream))
                    //{
                    //    string line;
                    //    while((line=sr.ReadLine()) != null)
                    //    {
                    //        //Console.WriteLine(line);
                    //        if(line == "END")
                    //        {
                    //            break;
                    //        }
                    //    }
                    //}
                    PDSMetadata meta = new PDSMetadata(stream);
                });

                if (i++ > 30)
                {
                    break;
                }
                //var s = "s3://landform/small.txt";
                //Console.WriteLine(s);
                //sh.GetStorageStream(s, stream =>
                //{
                //    using (var sr = new StreamReader(stream))
                //    {
                //        int offset = 1;

                //        byte[] buff = new byte[17];
                //        while (true)
                //        {
                //            int i = stream.Read(buff, offset, buff.Length - offset);

                //            if (i == 0)
                //            {
                //                break;
                //            }
                //            for (int j = offset; j < i + offset; j++)
                //            {
                //                char c = (char)buff[j];
                //                Console.Write(c);
                //            }
                //        }
                //        Console.WriteLine(sr.ReadLine());
                //    }

                //});
                //    Console.WriteLine(s);
                //    byte[] buf = new byte[200];
                //    sh.GetStream(s, stream =>
                //    {

                //        stream.Read(buf, 0, 100);
                //        //PDSMetadata meta = new PDSMetadata(stream);

                //    });
                //});    
            }
            timer.Stop();
            Console.WriteLine(timer.ElapsedMilliseconds);
            //foreach (var s in sh.SearchObjects("s3://m20-ocs-dev/ods/surface/sol/00583/opgs/rdr/ncam/", "*.IMG", false))
            //{
            //    Console.WriteLine(s);
            //}
            */

            int returnCode = Commands.RunFromCommandline(args);
            return returnCode;
        }
    }
}
