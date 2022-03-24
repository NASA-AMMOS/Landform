using System;

namespace JPLOPS.Pipeline
{
    public class SolDateTime
    {
        /// <summary>
        /// Convert Curiosity/MSL LMST (Local Mean Soloar Time) to an Earth UTC date time
        /// </summary>
        /// <param name="solDate"></param>
        /// <returns></returns>
        public static DateTime MSLSolLMSTToUTC(int solDate)
        {
            const double EARTH_SECS_PER_MARS_SEC = 1.02749125;

            // Sol 0 midnight = UTC 1:50PM, August 5th
            DateTime sol0Midnight = new DateTime(2012, 8, 5, 13, 50, 0, DateTimeKind.Utc);

            double elapsedEarthSecondsSinceMissionBeginning = (double)solDate * 3600.0 * 24.0 * EARTH_SECS_PER_MARS_SEC;

            DateTime currentEarthTimeUTC = sol0Midnight.AddSeconds(elapsedEarthSecondsSinceMissionBeginning);

            return currentEarthTimeUTC;
        }
    }
}
