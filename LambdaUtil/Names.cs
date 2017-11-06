using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


namespace Lambda.LambdaUtil
{
    //Keep me up-to-date with Cloud/Util/Names
    //Note: In Visual Studio 2017, it should be easy(ish) to compile a project to both .NET Framework and .NET Core. Sharing names!!!


    // TODO : 
    // For dynamo, we can either do json -> obj conversions for all communications with table 
    // or just use document model and save the strings here. I think that's better - i don't think we have enough complexity to justify the setup complexity of classes
    // (especailly since the built in version of that, dynamodbv2.datamodel, doesn't allow us to specify the table name at runtime (need for transfering between stacks)) 

    /// Column names within table for Pipeline (Mesh Metadata table)
    public class TableNames
    {
        public const string PARENT_MESH_ID_FIELD = "mesh_name";
        public const string CHILDREN = "children"; //a map from childId -> 0. Eventually map to version #
        public const string BUCKET = "bucket";
        public const string NUM_CHILDREN = "numchildren";

        //As a reminder to stop hardcoding this in the near future... 
        public const string HARDCODED_4 = "4";
    }

    /// <summary>
    /// Message fields in allignment job queue messages 
    /// Keep in sync with names in Pipeline/Commands/ImageIntake.cs
    /// </summary>
    public class AlignmentMessageFields
    {
        public const string MSG_TYPE_FIELD = "MessageType";
        public const string FILE_S3_PATH = "FileS3Path";
        public const string OBSERVATION_NAME = "ObservationName";
        public const string OBSERVATION_NAME_2 = "ObservationName2";
    }

    /// <summary>
    /// Message types for communication between lambda and allignment workers 
    /// Keep in sync with names in Pipeline/Commands/ImageIntake.cs
    /// </summary>
    public class AlignmentMessageTypes
    {
        public const string NEW_IMAGE_MSG = "NEW_IMAGE";
        public const string FIND_OVERLAPS_MSG = "FIND_OVERLAPS";
        public const string MATCH_PAIR_MSG = "MATCH_PAIR";
    }
    
}
