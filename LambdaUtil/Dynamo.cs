using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DataModel;

namespace Lambda.LambdaUtil
{
    // TODO : 
    // we can either do json -> obj conversions for all communications with table 
    // or just use document model and save the strings here. I think that's better - i don't think we have enough complexity to justify the complexity of casting all over the place
    // (especailly since the built in version of that, dynamodbv2.datamodel, doesn't allow us to specify the name at runtime (need for transfering between stacks)) 

    /// Column names within table 
    public class TableNames
    {
        public const string PARENT_MESH_ID_FIELD = "mesh_name";
        public const string CHILDREN = "children"; //a map from childId -> 0. Eventually map to version #
        public const string BUCKET = "bucket";
        public const string NUM_CHILDREN = "numchildren";

        //As a reminder to stop hardcoding this in the near future... 
        public const string HARDCODED_4 = "4";
    }
    
}
