using OPS.Alignment;
using OPS.Cloud;
using OPS.Pipeline.TileServer;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.AlignmentServer
{
    public class IngestImageMessage : TilingQueueMessage
    {
        public ImageRef SourceImage { get; set; }
    }

    public class CreateMaskMessage : TilingQueueMessage
    {
        public ImageRef Image { get; set; }
        public string Project { get; set; }
    }

    public class MaskCreatedMessage : TilingQueueMessage
    {
        public ImageRef Image { get; set; }
        public string Project { get; set; }
        public Guid MaskGuid { get; set; }
    }

    public class DetectFeaturesMessage : TilingQueueMessage
    {
        public ImageRef Image { get; set; }
        public string Project { get; set; }
        public Guid MaskGuid { get; set; }
    }

    public class FeaturesDetectedMessage : TilingQueueMessage
    {
        public ImageRef Image { get; set; }
        public string Project { get; set; }
        public Guid MaskGuid { get; set; }
        public Guid FeaturesGuid { get; set; }
    }

    public class MatchImagesMessage : TilingQueueMessage
    {
        public string Project { get; set; }

        public ImageRef ModelImage { get; set; }
        public string ModelFrameName { get; set; }
        public Guid ModelFeaturesGuid { get; set; }

        public ImageRef DataImage { get; set; }
        public string DataFrameName { get; set; }
        public Guid DataFeaturesGuid { get; set; }

        public int OverlapIndex { get; set; }
    }

    public class ImagesMatchedMessage : TilingQueueMessage
    {
        public string Project { get; set; }
        public ImageRef ModelImage { get; set; }
        public ImageRef DataImage { get; set; }
        public int OverlapIndex { get; set; }

        public Guid CorrespondenceGuid { get; set; }
    }
}
