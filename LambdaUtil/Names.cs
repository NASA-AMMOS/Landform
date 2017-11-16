using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


namespace Lambda.LambdaUtil
{
    //This namespace stores all AWS names (message types, fields, dynamo column names, etc) that need to be kept consistent between Landform and Lambda code. 
    //Also contains names shared between lambdas 
    //Names that are NOT shared with lambdas (eg message types that are only created/read by workers) are not included here

    /// Column names within table for Mesh tiling pipeline (Mesh Metadata table)
    ///          Shared between LambdaS3TileIntake (which uploads metadata entries) and 
    ///          LambdaDynamoProcessing (which creates CreateParentTile messages from complete metadata entries)
    public class TableNames
    {
        //Currently, specifying #children for a parent tile is difficult, so LambdaS3TileIntake enters 4 for all parent tiles. 
        //TODO: use REST API to specify this for a job
        public const int HARDCODED_4 = 4;
    }

    /// <summary>
    /// Message fields in the message for a new observation in the Alignment pipeline
    /// Keep in sync with message type in Cloud/Messages/NewObservationMessage
    /// </summary>
    public class NewObservationMsgFields
    {
        public const string MSG_TYPE_FIELD = "MessageType";
        public const string FILE_S3_PATH = "FileS3Path";
        public const string OBSERVATION_NAME = "ObservationName";
    }

    /// <summary>
    /// Message fields in the message for a new observation in the Alignment pipeline
    /// Keep in sync with message type in Cloud/Messages/CreateParentTileMsg
    /// </summary>
    public class CreateParentTileMsgFields
    {
        public const string MSG_TYPE_FIELD = "MessageType";
        public const string PARENT_PATH = "ParentPath";
        public const string NUM_CHILDREN = "NumChildren";
    }

    /// <summary>
    /// Message types for communication between lambda and allignment workers 
    /// Keep in sync with names in Pipeline/Commands/ImageIntake.cs
    /// </summary>
    public class PipelineMessageTypes
    {
        public const string NEW_OBSERVATION_MESSAGE = "NEW_IMAGE";
        public const string CREATE_PARENT_TILE_MSG = "CREATE_PARENT_TILE";
    }
    
}
