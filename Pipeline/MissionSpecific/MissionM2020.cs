using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{
    public class MissionM2020 : MissionSpecific
    {
        public const int EECAM_DOWNSAMPLE_FIELD = 46;
        public const int EECAM_RECONSTRUCTION_FIELD = 47;
        public const int DOWNSAMPLE_FIELD = 48;
        public const int COMPRESSION_FIELD = 49;
        public const int COMPRESSION_FIELD_LENGTH = 2;
        public const int VERSION_FIELD = 52;
        public const int VERSION_FIELD_LENGTH = 2;

        public override Mission GetMission()
        {
            return Mission.M2020;
        }

        //some images have invalid PLANET_DAY_NUMBER
        //we have seen this in multiple M2020 datasets so far including ROASTT19 and TT4
        public override int DayNumber(PDSParser parser)
        {
            try
            {
                return parser.PlanetDayNumber;
            }
            catch (MetadataException)
            {
                return ParseProductId(parser.ProductIdString).GetSol();
            }
        }

        public override RoverProductCamera TranslateCamera(RoverProductCamera cam)
        {
            switch (cam)
            {
                //in early datasets ML and MR in RDR product names for M2020 really mean MastcamZ not Mastcam
                //and in any case M2020 has only MastcamZ not Mastcam
                case RoverProductCamera.MastcamLeft: return RoverProductCamera.MastcamZLeft;
                case RoverProductCamera.MastcamRight: return RoverProductCamera.MastcamZRight;
                default: return cam;
            }
        }

        public override double GetSensorPixelSizeMM(RoverProductCamera camera) {
            throw new NotImplementedException("sensor pixels size not implemented for 2020 instruments yet");
        }

        public override double GetFocalLengthMM(RoverProductCamera rovProdCam)
        {
            throw new NotImplementedException("focal lengths not implemented for 2020 instruments yet");
        }

        public override double GetMinimumFocusDistance(PDSMetadata metadata)
        {
            throw new NotImplementedException("min focus distance not implemented for 2020 instruments yet");
        }

        public override double? GetMaximumFocusDistance(PDSMetadata metadata)
        {
            throw new NotImplementedException("max focus distance not implemented for 2020 instruments yet");
        }

        public override RoverObservationComparator GetRoverObservationComparator()
        {
            // 0 if a and b are equivalently good
            // negative if a is "better" than b
            // positive if a is "worse than" b
            int ext(RoverObservation a, RoverObservation b)
            {
                if (IsHazcam(a.Camera) || IsNavcam(a.Camera))
                {
                    //EECAM downsampling A,L,M,N, prefer higher
                    char edsA = a.Name[EECAM_DOWNSAMPLE_FIELD];
                    char edsB = b.Name[EECAM_DOWNSAMPLE_FIELD];
                    if (edsA != edsB)
                    {
                        return edsB - edsA;
                    }

                    //EECAM reconstruction counter 0-9A-Z, prefer higher
                    char rcA = a.Name[EECAM_RECONSTRUCTION_FIELD];
                    char rcB = b.Name[EECAM_RECONSTRUCTION_FIELD];
                    if (rcA != rcB)
                    {
                        return rcB - rcA;
                    }
                }

                //downsample 0-3, prefer lower
                char dsA = a.Name[DOWNSAMPLE_FIELD];
                char dsB = b.Name[DOWNSAMPLE_FIELD];
                if (dsA != dsB)
                {
                    return dsA - dsB;
                }

                //compresion, prefer higher
                int compA = CompressionPreference(a.Name.Substring(COMPRESSION_FIELD, COMPRESSION_FIELD_LENGTH));
                int compB = CompressionPreference(b.Name.Substring(COMPRESSION_FIELD, COMPRESSION_FIELD_LENGTH));
                if (compA != compB)
                {
                    return compB - compA;
                }

                return 0;
            }

            return new RoverObservationComparator(PreferMSSSToOPGS(),
                                                  PreferLinearToNonlinear(),
                                                  PreferColorToGrayscale(),
                                                  PreferEyeForGeometry(),
                                                  this,
                                                  ext);
        }

        public override IEnumerable<RoverProductId> FilterProductIdGroups(IEnumerable<RoverProductId> products)
        {
            Func<RoverProductId, bool> isEECAM = id => IsHazcam(id.Camera) || IsNavcam(id.Camera);
            var groups = products.GroupBy(id => id.GetPartialId(this, includeVariants: false, includeVersion: false));
            foreach (var group in groups)
            {
                var filtered = group.Select(id => id);

                //EECAM downsampling A,L,M,N, prefer higher
                //note the SIS changed to allow only A or M here, but this code should remain correct (prefer M over A)
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/852
                //though also see https://github.jpl.nasa.gov/OnSight/Landform/issues/891
                char maxEDS = filtered
                    .Where(id => isEECAM(id))
                    .Select(id => id.FullId[EECAM_DOWNSAMPLE_FIELD])
                    .DefaultIfEmpty('0')
                    .Max();
                filtered = filtered.Where(id => !isEECAM(id) || id.FullId[EECAM_DOWNSAMPLE_FIELD] == maxEDS);

                //EECAM reconstruction counter 0-9A-Z, prefer higher
                char maxERC = filtered
                    .Where(id => isEECAM(id))
                    .Select(id => id.FullId[EECAM_RECONSTRUCTION_FIELD])
                    .DefaultIfEmpty('0')
                    .Max();
                filtered = filtered.Where(id => !isEECAM(id) || id.FullId[EECAM_RECONSTRUCTION_FIELD] == maxERC);

                //downsample 0-3, prefer lower
                char minDS = filtered.Select(id => id.FullId[DOWNSAMPLE_FIELD]).DefaultIfEmpty('0').Min();
                filtered = filtered.Where(id => id.FullId[DOWNSAMPLE_FIELD] == minDS);

                //compresion, prefer higher
                int maxCP = filtered.Select(id => CompressionPreference(id)).DefaultIfEmpty(0).Max();
                filtered = filtered.Where(id => CompressionPreference(id) == maxCP);

                foreach (var id in filtered)
                {
                    yield return id;
                }
            }
        }

        public int CompressionPreference(RoverProductId id)
        {
            int cf = COMPRESSION_FIELD, cfl = COMPRESSION_FIELD_LENGTH;
            return CompressionPreference(id.GetPartialId(cf, cfl));
        }

        public int CompressionPreference(string compression)
        {
            compression = compression.ToUpper();
            if (compression.StartsWith("L")) //lossless
            {
                return 300;
            }
            else if (compression.StartsWith("I")) //ICER
            {
                return 200;
            }
            else if (compression == "A0") //JPEG quality 100
            {
                return 100;
            }
            else if (int.TryParse(compression, out int jpegQuality))
            {
                return jpegQuality;
            }
            else
            {
                return -1;
            }
        }

        public override RoverMasker GetMasker()
        {
            return new M2020RoverMasker(this); //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/554
        }

        public override bool IsHazcam(RoverProductCamera camera)
        {
            return base.IsHazcam(camera) ||
                camera == RoverProductCamera.FrontHazcamLeftB || camera == RoverProductCamera.FrontHazcamRightB;
        }

        public override bool IsMastcam(RoverProductCamera camera)
        {
            return base.IsMastcam(camera) ||
                camera == RoverProductCamera.MastcamZLeft || camera == RoverProductCamera.MastcamZRight;
        }

        public override bool IsArmcam(RoverProductCamera camera)
        {
            //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/897
            return camera == RoverProductCamera.SHERLOCACI ||
                camera == RoverProductCamera.SHERLOCWATSON ||
                camera == RoverProductCamera.SHERLOCWATSONLeft || camera == RoverProductCamera.SHERLOCWATSONRight;
        }

        public override bool AllowPlacesDB()
        {
            return false; //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/535
        }

        public override RoverProductId ParseProductId(string id)
        {
            id = StringHelper.GetLastUrlPathSegment(id, stripExtension: true);

            //TODO for now the M2020 SIS for unified mesh product IDs is incomplete
            //and M2020 datasets we're working with so far that have unified meshes seem to use the MSL format
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/793
            //MSL unified mesh IDs can be from 32 to 36 chars long
            //Unfortunately regular MSL IDs are 36 chars long - first try as unified
            if (id.Length >= MSLUnifiedMeshProductId.MIN_LENGTH && id.Length <= MSLUnifiedMeshProductId.MAX_LENGTH)
            {
                var unified = MSLUnifiedMeshProductId.Parse(id);
                if (unified != null)
                {
                    return unified;
                }
            }

            switch (id.Length)
            {
                case M2020OPGSProductId.LENGTH: return M2020OPGSProductId.Parse(id);
                default: throw new Exception("unexpected length for M2020 product id");
            }
        }

        public override bool CheckProductId(RoverProductId id, out string reason)
        {
            if (!base.CheckProductId(id, out reason))
            {
                return false;
            }

            if (id is M2020OPGSProductId)
            {
                M2020OPGSProductId opgsId = (M2020OPGSProductId)id;
                string spec = opgsId.Spec.ToUpper();
                if (spec != "_")
                {
                    //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/895
                    reason = "special processing " + spec;
                    return false;
                }

                var camspec = opgsId.Camspec.ToUpper();
                if (IsHazcam(id.Camera) || IsNavcam(id.Camera) || IsMastcam(id.Camera))
                {
                    var stereoPartner = camspec.Substring(0, 1);
                    if (stereoPartner != "_")
                    {
                        //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/883
                        reason = "stereo partner " + stereoPartner;
                    }
                }

                //downsample and compression handled in RoverObservationComparator

                if (opgsId.Color == RoverProductColor.Unknown)
                {
                    reason = "color filter " + opgsId.ColorFilter;
                    return false;
                }
            }

            if (id.Producer == RoverProductProducer.MSSS)
            {
                //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/754
                return false;
            }

            return true;
        }

        public override IEnumerable<int[]> GetProductIdVariantSpans(RoverProductId id)
        {
            if (id is M2020OPGSProductId)
            {
                yield return new int[] { EECAM_DOWNSAMPLE_FIELD, 1 };
                yield return new int[] { EECAM_RECONSTRUCTION_FIELD, 1 };
                yield return new int[] { DOWNSAMPLE_FIELD, 1 };
                yield return new int[] { COMPRESSION_FIELD, COMPRESSION_FIELD_LENGTH };
            }
            yield break;
        }

        public override string GetTacticalMeshQueueName()
        {
            return "m20-ids-g-sqs-landform-iandt2";
        }

        public override string GetTacticalMeshFailQueueName()
        {
            return "m20-ids-g-sqs-landform-iandt2-fail";
        }

        public override QueueMessage DequeueTacticalMeshMessage(MessageQueue queue)
        {
            return queue.DequeueOne<SNSMessageWrapper>();
        }

        public override string GetUrlFromTacticalMeshQueueMessage(QueueMessage msg, bool filter = true,
                                                                  ILogger logger = null)
        {
            if (!(msg is SNSMessageWrapper))
            {
                throw new Exception("M2020 tactical mesh queue message does not have SNS wrapper");
            }

            var s3Msg = JsonHelper.FromJson<S3EventMessage>(((SNSMessageWrapper)msg).Message);
            if (s3Msg.Records.Count != 1)
            {
                throw new Exception(string.Format("M2020 tactical mesh queue message has {0} records, expected 1",
                                                  s3Msg.Records.Count));
            }

            var s3EventRecord = s3Msg.Records[0];
            string prefix = "ObjectCreated";
            if (!s3EventRecord.eventName.StartsWith(prefix))
            {
                throw new Exception(string.Format("M2020 tactical mesh queue message event name \"{0}\", " +
                                                  "expected \"{1}*\"", s3EventRecord.eventName, prefix));
            }

            var s3EventData = s3EventRecord.s3;
            if (s3EventData == null)
            {
                throw new Exception("M2020 tactical mesh queue message has no S3EventData");
            }

            if (s3EventData.bucket == null)
            {
                throw new Exception("M2020 tactical mesh queue message has no S3 bucket");
            }

            if (s3EventData.obj == null)
            {
                throw new Exception("M2020 tactical mesh queue message has no S3 object");
            }

            string url = string.Format("s3://{0}/{1}", s3EventData.bucket.name,
                                       s3EventData.obj.key); //already url encoded

            if (filter)
            {
                //OBJ is written last, only keep OBJ messages
                if (!url.EndsWith(".obj", ignoreCase: true, culture: null))
                {
                    if (logger != null)
                    {
                        logger.LogInfo("ignoring M2020 tactical mesh queue message (not OBJ) {0}", url);
                    }
                    return null;
                }

                var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                var id = RoverProductId.Parse(idStr, this, throwOnFail: false) as M2020OPGSProductId;
                if (id == null)
                {
                    if (logger != null)
                    {
                        logger.LogInfo("ignoring M2020 tactical mesh queue message (bad product ID) {0}", url);
                    }
                    return null;
                }

                if (id.Size == RoverProductSize.Thumbnail)
                {
                    if (logger != null)
                    {
                        logger.LogInfo("ignoring M2020 tactical mesh queue message (thumbnail) {0}", url);
                    }
                    return null;
                }
            }

            return url;
        }

        public override QueueMessage ParseTacticalMeshQueueMessage(string json)
        {
            return JsonHelper.FromJson<SNSMessageWrapper>(json, autoTypes: false);
        }

        public override string GetContextualMeshQueueName()
        {
            throw new NotImplementedException(); //TODO
        }

        public override string GetContextualMeshFailQueueName()
        {
            throw new NotImplementedException(); //TODO
        }

        public override QueueMessage DequeueContextualMeshMessage(MessageQueue queue)
        {
            throw new NotImplementedException(); //TODO
        }

        public override ContextualMeshParameters GetParametersFromContextualMeshQueueMessage(QueueMessage msg)
        {
            throw new NotImplementedException(); //TODO
        }

        public override QueueMessage ParseContextualMeshQueueMessage(string json)
        {
            throw new NotImplementedException(); //TODO
        }

        public override string GetS3Proxy()
        {
            return "https://data.m20-dev.jpl.nasa.gov";
        }

        public override float GetDemMetersPerPixel()
        {
            throw new NotImplementedException();
        }

        public override Vector2 GetSiteDriveOriginPixelInDem(SiteDrive siteDrive, string demFilePath = null)
        {
            throw new NotImplementedException();
        }
    }

    public class MissionROASTT19 : MissionM2020 
    {
        public override Mission GetMission()
        {
            return Mission.ROASTT19;
        }

        // ROASTT19: bug prevents RMC from being used for frame names. This workaround
        // will break multiple images with different filters resolving to same frame.
        public override string RoverMotionCounter(PDSParser parser)
        {          
            return ((M2020OPGSProductId)ParseProductId(parser.ProductIdString)).GetConcatenatedTimeString();
        }

        // ROASTT19: for some images the INSTRUMENT_ID says LEFT when it should say RIGHT, so use PRODUCT_ID instead
        public override RoverProductCamera GetCamera(PDSParser parser)
        {
            return TranslateCamera(ParseProductId(parser.ProductIdString).Camera);
        }
    }

    public class MissionTT4 : MissionM2020
    {
        public const int SEQUENCE_FIELD = 35; 
        public const int SEQUENCE_FIELD_LENGTH = 9; 

        public override Mission GetMission()
        {
            return Mission.TT4;
        }

        //TT4: sequence number is bumped for different variants
        public override IEnumerable<int[]> GetProductIdVariantSpans(RoverProductId id)
        {
            foreach (var span in base.GetProductIdVariantSpans(id))
            {
                yield return span;
            }
            yield return new int[] { SEQUENCE_FIELD, SEQUENCE_FIELD_LENGTH };
            yield break;
        }
    }

    public class MissionScarecrowEECAM : MissionM2020
    {
        private class ScarecrowEECAMUnifiedMesh : OPGSProductId
        {
            public const int LENGTH = 10;

            protected ScarecrowEECAMUnifiedMesh(string fullId, int site, int drive)
                : base(fullId, RoverProductProducer.OPGS, RoverProductType.Points, camera: "NL", geometry: "L",
                       color: "", version: "0", size: "", site: site, drive: drive) 
            { }

            public static ScarecrowEECAMUnifiedMesh Parse(string id)
            {
                id = StringHelper.StripUrlExtension(id);
                if (id.Length != LENGTH)
                {
                    return null;
                }

                string siteStr = id.Substring(3, 3);
                string driveStr = id.Substring(6, 4);

                if (!int.TryParse(siteStr, out int site) || !int.TryParse(driveStr, out int drive))
                {
                    return null;
                }
                
                return new ScarecrowEECAMUnifiedMesh(id, site, drive);
            }

            public override bool IsSingleFrame()
            {
                return false;
            }
            
            public override bool IsSingleCamera()
            {
                return true;
            }
            
            public override bool IsSingleSiteDrive()
            {
                return true;
            }

            protected override RoverProductColor ParseColor(string color, string camera)
            {
                return RoverProductColor.Unknown;
            }

            public override string AsThumbnail()
            {
                throw new NotImplementedException();
            }

            protected override RoverProductSize ParseSize(string size)
            {
                return RoverProductSize.Regular;
            }

            public override int GetSol()
            {
                return 0;
            }
        }

        public override RoverProductId ParseProductId(string id)
        {
            id = StringHelper.GetLastUrlPathSegment(id, stripExtension: true);
            if (id.Length == ScarecrowEECAMUnifiedMesh.LENGTH)
            {
                return ScarecrowEECAMUnifiedMesh.Parse(id);
            }
            return base.ParseProductId(id);
        }
    }

    public class MissionROASTT20 : MissionM2020
    {
        public override Mission GetMission()
        {
            return Mission.ROASTT20;
        }

        //ROASTT20: RSM counter not incremented in dataset
        public override string GetObservationFrameName(PDSParser parser)
        {
            double[] angles = parser.metadata.ReadAsDoubleArray("RSM_ARTICULATION_STATE", "ARTICULATION_DEVICE_ANGLE");
            var name = string.Format("{0}_{1}", GetCamera(parser), RoverMotionCounter(parser));

            //first two angles are mast
            foreach (var angle in angles.Take(2))
            {
                name += "_" + angle.ToString();
            }
            return name;
        }

        public override string GetS3Proxy()
        {
            return "https://data-roastt.m20-training.jpl.nasa.gov";
        }
    }
}
