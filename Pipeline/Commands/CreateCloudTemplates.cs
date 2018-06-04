using CommandLine;
using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Amazon.DynamoDBv2.DataModel;
using System.Reflection;
using OPS.Cloud;

namespace OPS.Pipeline
{

    [Verb("create-cloud-templates", HelpText = "Create CloudFormation templates for all DynamoDB tables.")]
    public class CreateCloudTemplatesOptions
    {
    }

    class SecondaryGlobalIndex
    {
        public string Name;
        public string HashKey;
        public string RangeKey;
        public SecondaryGlobalIndex(string name)
        {
            Name = name;
            HashKey = null;
            RangeKey = null;
        }
    }

    public class CreateCloudTemplates
    {
        CreateCloudTemplatesOptions Options;
        public CreateCloudTemplates(CreateCloudTemplatesOptions options)
        {
            Options = options;
        }

        static JArray CreateKeySchema(string hashKey, string rangeKey)
        {
            var res = new JArray();
            if (hashKey != null)
            {
                res.Add(new JObject
                {
                    { "AttributeName", hashKey },
                    { "KeyType", "HASH" }
                });
            }
            if (rangeKey != null)
            {
                res.Add(new JObject
                {
                    { "AttributeName", rangeKey },
                    { "KeyType", "RANGE" }
                });
            }
            return res;
        }

        static JToken MakeTableName(string nameSuffix)
        {
            return new JObject
            {
                ["Fn::Join"] = new JArray
                {
                    "",
                    new JArray
                    {
                        new JObject { ["Ref"] = "TablePrefix" },
                        nameSuffix
                    }
                }
            };
        }

        static JToken MakeTableRef(string name)
        {
            return new JObject
            {
                ["Fn::Join"] = new JArray
                {
                    "/",
                    new JArray
                    {
                        "table",
                        new JObject { ["Ref"] = name }
                    }
                }
            };
        }

        JObject CreateResourcesForTable(Type type)
        {
            JObject res = new JObject();

            var ta = type.GetCustomAttribute<DynamoDBTableAttribute>();
            if (ta == null)
            {
                return res;
            }

            res["Resources"] = new JObject();
            res["Outputs"] = new JObject();

            string rangeKey = null;
            string hashKey = null;
            Dictionary<string, JObject> allProps = new Dictionary<string, JObject>();
            Dictionary<string, SecondaryGlobalIndex> secondaryIndices = new Dictionary<string, SecondaryGlobalIndex>();

            var properties = new JArray();
            foreach (var field in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // get name of corresponding DynamoDB field
                var fieldName = field.Name;
                {
                    foreach (var pa in field.GetCustomAttributes<DynamoDBPropertyAttribute>())
                    {
                        if (pa != null && pa.AttributeName != null)
                        {
                            fieldName = pa.AttributeName;
                            break;
                        }
                    }
                }

                // get field properties
                bool isNum = field.GetType() == typeof(int) || field.GetType() == typeof(float) || field.GetType() == typeof(double);
                allProps[fieldName] = new JObject
                {
                    { "AttributeName", fieldName },
                    { "AttributeType", isNum ? "N" : "S" }
                };

                // get hash and range key, if defined
                // DynamoDBGlobalSecondaryIndex[Hash,Range]KeyAttribute are subclasses of DynamoDB[Hash,Range]KeyAttribute, make sure not to use those
                if (field.GetCustomAttributes<DynamoDBHashKeyAttribute>().Where(a => a.GetType() == typeof(DynamoDBHashKeyAttribute)).Any())
                {
                    hashKey = fieldName;
                }
                if (field.GetCustomAttributes<DynamoDBRangeKeyAttribute>().Where(a => a.GetType() == typeof(DynamoDBRangeKeyAttribute)).Any())
                {
                    rangeKey = fieldName;
                }

                // create any secondary indices and connect them to hash and range keys
                Func<string, SecondaryGlobalIndex> getIndex = (indexName) =>
                {
                    if (!secondaryIndices.ContainsKey(indexName))
                    {
                        secondaryIndices[indexName] = new SecondaryGlobalIndex(indexName);
                    }
                    return secondaryIndices[indexName];
                };
                var sihka = field.GetCustomAttribute<DynamoDBGlobalSecondaryIndexHashKeyAttribute>();
                if (sihka != null)
                {
                    foreach (var indexName in sihka.IndexNames)
                    {
                        getIndex(indexName).HashKey = fieldName;
                    }
                }
                var sirka = field.GetCustomAttribute<DynamoDBGlobalSecondaryIndexRangeKeyAttribute>();
                if (sirka != null)
                {
                    foreach (var indexName in sirka.IndexNames)
                    {
                        getIndex(indexName).RangeKey = fieldName;
                    }
                }
            }

            // must define *only* attributes used in hash or range keys
            HashSet<string> definedAttrs = new HashSet<string>();
            if (hashKey != null)
            {
                definedAttrs.Add(hashKey);
            }
            if (rangeKey != null)
            {
                definedAttrs.Add(rangeKey);
            }
            foreach (var secondaryIndex in secondaryIndices.Values)
            {
                if (secondaryIndex.HashKey != null)
                {
                    definedAttrs.Add(secondaryIndex.HashKey);
                }
                if (secondaryIndex.RangeKey != null)
                {
                    definedAttrs.Add(secondaryIndex.RangeKey);
                }
            }
            var attribDefs = new JArray();
            foreach (var attrName in definedAttrs)
            {
                attribDefs.Add(allProps[attrName]);
            }

            // determine capacity
            var readCapacity = type.GetCustomAttribute<DynamoDBReadCapacityAttribute>();
            var writeCapacity = type.GetCustomAttribute<DynamoDBWriteCapacityAttribute>();
            Func<DynamoDBCapacityAttribute, int, int> capacityToDefine = (cap, defaultValue) =>
            {
                if (cap == null)
                {
                    return defaultValue;
                }
                return cap.Fixed ? cap.FixedCapacity : cap.MinCapacity;
            };

            var tableDef = new JObject
            {
                { "Type", "AWS::DynamoDB::Table" },
                {
                    "Properties", new JObject
                    {
                        { "TableName", MakeTableName(ta.TableName) },
                        { "AttributeDefinitions", attribDefs },
                        { "KeySchema", CreateKeySchema(hashKey, rangeKey) },
                        {
                            "ProvisionedThroughput", new JObject
                            {
                                { "ReadCapacityUnits", capacityToDefine(readCapacity, 50) },
                                { "WriteCapacityUnits", capacityToDefine(writeCapacity, 5) }
                            }
                        }
                    }
                }
            };

            if (secondaryIndices.Count > 0)
            {
                var secIndices = new JArray();
                foreach (var index in secondaryIndices.Values)
                {
                    secIndices.Add(new JObject
                    {
                        { "IndexName", index.Name },
                        { "KeySchema", CreateKeySchema(index.HashKey, index.RangeKey)},
                        {
                            "Projection", new JObject
                            {
                                { "ProjectionType", "ALL" }
                            }
                        },
                        {
                            "ProvisionedThroughput", new JObject
                            {
                                { "ReadCapacityUnits", 50 },
                                { "WriteCapacityUnits", 5 }
                            }
                        }
                    });
                }
                tableDef["Properties"]["GlobalSecondaryIndexes"] = secIndices;
            }

            res["Resources"][ta.TableName] = tableDef;
            res["Outputs"][ta.TableName] = new JObject
            {
                { "Description", "Table " + ta.TableName },
                { "Value", new JObject { { "Ref", ta.TableName } } }
            };

            // if dynamic capacity is desired, create scaling targets and policies
            foreach (var kvp in CreateScalingStuff(ta.TableName, readCapacity, "Read"))
            {
                res["Resources"][kvp.Key] = kvp.Value;
            }
            foreach (var kvp in CreateScalingStuff(ta.TableName, writeCapacity, "Write"))
            {
                res["Resources"][kvp.Key] = kvp.Value;
            }

            return res;
        }

        private static IEnumerable<KeyValuePair<string, JObject>> CreateScalingStuff(string tableName, DynamoDBCapacityAttribute capacity, string name)
        {
            if (capacity == null || capacity.Fixed)
            {
                yield break;
            }
            var target = new JObject
            {
                { "Type", "AWS::ApplicationAutoScaling::ScalableTarget" },
                { "Properties", new JObject
                    {
                        { "MaxCapacity", capacity.MaxCapacity },
                        { "MinCapacity", capacity.MinCapacity },
                        { "ResourceId", MakeTableRef(tableName) },
                        { "RoleARN", new JObject { ["Ref"] = "TableAutoscalingRoleArn" } },
                        { "ScalableDimension", "dynamodb:table:"+name+"CapacityUnits" },
                        { "ServiceNamespace", "dynamodb" }
                    }
                }
            };
            var targetName = tableName + name + "Target";
            yield return new KeyValuePair<string, JObject>(targetName, target);
            var policy = new JObject
            {
                { "Type", "AWS::ApplicationAutoScaling::ScalingPolicy" },
                { "Properties", new JObject
                    {
                        { "PolicyName", name + "AutoScalingPolicy"},
                        { "PolicyType", "TargetTrackingScaling"},
                        { "ScalingTargetId", new JObject { ["Ref"] = targetName } },
                        { "TargetTrackingScalingPolicyConfiguration", new JObject
                            {
                                { "TargetValue", 50 },
                                { "ScaleInCooldown", 60 },
                                { "ScaleOutCooldown", 60 },
                                { "PredefinedMetricSpecification", new JObject { ["PredefinedMetricType"] = "DynamoDB"+name+"CapacityUtilization" } },
                            }
                        }
                    }
                }
            };
            var policyName = tableName + name + "Policy";
            yield return new KeyValuePair<string, JObject>(policyName, policy);
        }

        public int Run()
        {
            var dbTemplate = new JObject
            {
                { "AWSTemplateFormatVersion", "2010-09-09" },
                { "Resources", new JObject() },
                {
                    "Parameters", new JObject
                    {
                        { "TablePrefix", new JObject
                            {
                                { "Type", "String" },
                                { "Description", "The table name prefix. Workers will use this to publish to their tables even if multiple stacks run in the same region." }
                            }
                        },
                        { "TableAutoscalingRoleArn", new JObject
                            {
                                { "Type", "String" },
                                { "Default", "arn:aws:iam::589270964471:role/AutoscalingServiceRole" },
                                { "Description", "Role assumed by autoscaling agents for tables." }
                            }
                        }
                    }
                }
            };

            foreach (var dbt in new[]
            {
                typeof(Project), typeof(Observation), typeof(Frame), typeof(FrameTransform), typeof(TransformPrior), typeof(Overlap)
            })
            {
                dbTemplate.Merge(CreateResourcesForTable(dbt), new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Union });
            }

            Console.WriteLine(dbTemplate.ToString());
            return 0;
        }
    }
}
