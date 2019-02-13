using OPS.Imaging;
using OPS.Util;
using OPS.Pipeline.AlignmentServer;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OPS.Pipeline
{
    public class MSLProject
    {
        public const string ROOT_FRAME_NAME = "root";

        //constants for cutoffs
        public const int MIN_NAV_HAZ_EXPOSURE = 80;
        public const int MIN_MASTCAM_FOCUS_CUTOFF = 3;
        public const int MAX_MASTCAM_WIDTH = 1344;

        public static RoverObservation FindBestImage(IEnumerable<RoverObservation> frameObservations)
        {
            var list = frameObservations.Where(ob => ob.UseForReconstruction).ToList();
            if (list.Count > 0)
            {
                list.Sort(RoverObservationComparison);
                return list.First();
            }
            return null;
        }

        public static ImagePointPair FindBestPair(IEnumerable<RoverObservation> frameObservations)
        {
            var list = frameObservations.Where(ob => ob.UseForReconstruction).ToList();

            list.Sort(RoverObservationComparison);
            var imageList = list.Where(ob => ob.ObservationType == ObservationType.Image.ToString()).ToList();
            var pointList = list.Where(ob => ob.ObservationType == ObservationType.Points.ToString()).ToList();
            if (pointList.Count > 0)
            {
                foreach (var imageObs in imageList)
                {
                    bool linear = IsLinear(imageObs);
                    foreach (var pointObs in pointList)
                    {
                        if (linear == IsLinear(pointObs) && imageObs.Width == pointObs.Width && imageObs.Height == pointObs.Height)
                        {
                            return new ImagePointPair(imageObs, pointObs);
                        }
                    }
                }
            }
            // If we didn't find any range products to match our image products than just return the first image
            if (imageList.Count > 0)
            {
                return new ImagePointPair(imageList.First(), null);
            }
            return null;
        }

        public static IEnumerable<ImagePointPair> FindBestPairs(IEnumerable<RoverObservation> observations)
        {
            List<ImagePointPair> results = new List<ImagePointPair>();
            // Filter any that should not be used for observation
            observations = observations.Where(ob => ob.UseForReconstruction);
            var frameGroups = observations.GroupBy(ob => ob.FrameName);
            foreach (var frameGroup in frameGroups)
            {
                var r = FindBestPair(frameGroup);
                if (r != null)
                {
                    results.Add(r);
                }
            }
            return results;
        }
        
        public class ImagePointPair
        {
            public Observation Image;
            public Observation Point;

            public ImagePointPair() { }

            public ImagePointPair(Observation img, Observation pnt)
            {
                Image = img;
                Point = pnt;
            }
        }

        /// <summary>
        /// Return -1 if a < b
        /// return 1 if  a > b
        /// return 0 if equal
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static int RoverObservationComparison(RoverObservation a, RoverObservation b)
        {
            // sort first by producer
            if (a.Producer == RoverProductProducer.MSSS.ToString() && b.Producer == RoverProductProducer.OPGS.ToString())
            {
                return -1;
            }
            if (a.Producer == RoverProductProducer.OPGS.ToString() && b.Producer == RoverProductProducer.MSSS.ToString())
            {
                return 1;
            }
            // sort second by linear-ness
            var linearA = IsLinear(a);
            var linearB = IsLinear(b);
            if (!linearA && linearB)
            {
                return -1;
            }
            if (linearA && !linearB)
            {
                return 1;
            }

            // versions go numeric 1 to 9, A-Z, _ (opgs) and numeric 0 to 9, A-Z (msss)
            return (int)b.Version[0] - (int)a.Version[0];
        }
        
        public static bool IsLinear(RoverObservation observation)
        {
            return ((CameraModel)JsonHelper.FromJson(observation.CameraModel)).Linear;
        }
    }
}
