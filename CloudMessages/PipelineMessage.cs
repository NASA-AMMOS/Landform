using System;
using System.Collections.Generic;
using System.Linq;

using Amazon.SQS.Model;
using Amazon.SQS;
using System.Reflection;
using Newtonsoft.Json;

namespace OPS.Cloud
{
    public abstract class PipelineMessage
    {
        public string MessageId { get; protected set; }

        protected string receiptHandle;

        /// <summary>
        /// Return a message object of the appropriate type for this SQS message
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        public static PipelineMessage FromMessage(Message m)
        {
            string typeName = m.MessageAttributes["MessageType"].StringValue;
            if (!NameToType.ContainsKey(typeName))
            {
                throw new CloudException("Unrecognized message type \"" + typeName + "\"");
            }
            var type = NameToType[typeName];
            var typeData = RegisteredTypes[type];

            PipelineMessage res = (PipelineMessage)typeData.Construct();
            foreach (var field in typeData.Fields)
            {
                var value = field.Parse(m.Attributes[field.Name]);
                field.PropertyInfo.SetValue(res, value);
            }

            if (typeData.BodyField != null)
            {
                var field = typeData.BodyField;
                field.SetValue(res, JsonConvert.DeserializeObject(m.Body, field.PropertyType));
            }

            res.MessageId = m.MessageId;
            res.receiptHandle = m.ReceiptHandle;
            return res;
        }


        /// <summary>
        /// Delete this message 
        /// </summary>
        /// <param name="client"></param>
        /// <param name="queueUrl"></param>
        public void DeleteMessage(IAmazonSQS client, string queueUrl)
        {
            var delRequest = new DeleteMessageRequest
            {
                QueueUrl = queueUrl,
                ReceiptHandle = this.receiptHandle
            };
            
            var response = client.DeleteMessageAsync(delRequest).Result;
            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new CloudException("Could not delete message");
            }
        }

        /// <summary>
        /// Send this message to an SQS queue.
        /// </summary>
        /// <param name="client">SQS client</param>
        /// <param name="queueUrl">Queue URL</param>
        public string Send(IAmazonSQS client, string queueUrl)
        {
            var typeData = RegisteredTypes[this.GetType()];
            var dict = new Dictionary<string, MessageAttributeValue>();
            dict["MessageType"] = new MessageAttributeValue
            {
                DataType = "String",
                StringValue = typeData.Name
            };

            foreach (var field in typeData.Fields)
            {
                dict[field.Name] = new MessageAttributeValue
                {
                    DataType = field.DataType,
                    StringValue = field.PropertyInfo.GetValue(this).ToString()
                };
            }

            string messageBody = "{}";
            if (typeData.BodyField != null)
            {
                messageBody = JsonConvert.SerializeObject(typeData.BodyField.GetValue(this));
            }

            MessageId = Send(client, dict, queueUrl, messageBody, typeData.DelaySeconds);
            return MessageId;
        }

        public static bool QueueExists(IAmazonSQS client, string queueUrl)
        {
            GetQueueAttributesRequest req = new GetQueueAttributesRequest()
            {
                AttributeNames = new List<String> { "QueueArn" },
                QueueUrl = queueUrl
            };
            try
            {
                GetQueueAttributesResponse resp = client.GetQueueAttributesAsync(req).Result;
            }
            catch (AmazonSQSException e)
            {
                if (e.ErrorCode == "AWS.SimpleQueueService.NonExistentQueue")
                {
                    return false;
                }
                else throw e;
            }
            return true;
        }
        
        #region Reflection magic
        public string MessageType
        {
            get { return RegisteredTypes[GetType()].Name; }
        }

        internal struct FieldData
        {
            /// <summary>
            /// Name as serialized in an SQS message.
            /// </summary>
            public string Name;
            /// <summary>
            /// SQS datatype (almost always "String")
            /// </summary>
            public string DataType;
            /// <summary>
            /// PropertyInfo for accessing programmatically
            /// </summary>
            public PropertyInfo PropertyInfo;

            public object Parse(string value)
            {
                var destType = PropertyInfo.PropertyType;
                if (destType == typeof(int))
                {
                    return Convert.ToInt32(value);
                }
                else if (destType == typeof(float))
                {
                    return Convert.ToSingle(value);
                }
                else if (destType == typeof(double))
                {
                    return Convert.ToDouble(value);
                }
                
                var constructor = destType.GetTypeInfo().GetConstructor(new Type[] { typeof(string) });
                if (constructor != null)
                {
                    return constructor.Invoke(new object[] { value });
                }
                else
                {
                    return JsonConvert.DeserializeObject(value, destType);
                }
            }
        }

        internal struct TypeData
        {
            public string Name;
            public int DelaySeconds;
            public Type Type;
            public List<FieldData> Fields;
            public PropertyInfo BodyField;

            public object Construct(params object[] parameters)
            {
                var constructor = Type.GetTypeInfo().GetConstructor(parameters.Select(p => p.GetType()).ToArray());
                return constructor.Invoke(parameters);
            }
        }

        protected static string Send(IAmazonSQS client, Dictionary<string, MessageAttributeValue> attributes, string queueUrl, string messageBody, int delaySeconds)
        {
            SendMessageRequest request = new SendMessageRequest
            {
                DelaySeconds = delaySeconds,
                MessageAttributes = attributes,
                MessageBody = messageBody,
                QueueUrl = queueUrl
            };
            SendMessageResponse response = client.SendMessageAsync(request).Result;

            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new CloudException("Could not send message to queue");
            }

            return response.MessageId;
        }

        internal static Dictionary<string, Type> NameToType = new Dictionary<string, Type>();
        internal static Dictionary<Type, TypeData> RegisteredTypes = new Dictionary<Type, TypeData>();
        internal static void RegisterType(Type t)
        {
            if (RegisteredTypes.ContainsKey(t)) throw new Exception("Type " + t.Name + " already registered");
            var ti = t.GetTypeInfo();

            if (!ti.IsSubclassOf(typeof(PipelineMessage))) throw new Exception("Message type must derive from PipelineMessage");
            MessageAttribute na = (MessageAttribute)ti.GetCustomAttribute(typeof(MessageAttribute));
            if (na == null) throw new Exception("Pipeline message classes must have a MessageAttribute");
            string name = na.Name;
            if (NameToType.ContainsKey(name)) throw new Exception("Name \"" + name + "\" already registered");

            TypeData td = new TypeData();
            td.Name = name;
            td.DelaySeconds = na.DelaySeconds;
            td.Type = t;
            td.Fields = new List<FieldData>();
            foreach (var prop in t.GetProperties())
            {
                var ba = prop.GetCustomAttribute<BodyAttribute>(false);
                if (ba != null)
                {
                    if (td.BodyField != null) throw new Exception("Multiple BodyAttributes on message");
                    td.BodyField = prop;
                }

                var fa = prop.GetCustomAttribute<FieldAttribute>(false);
                if (fa != null)
                {
                    if (ba != null) throw new Exception("Can't have both BodyAttribute and FieldAttribute on same property");
                    FieldData fd = new FieldData();
                    fd.Name = fa.Name;
                    fd.DataType = fa.DataType;
                    fd.PropertyInfo = prop;
                    td.Fields.Add(fd);
                }
            }

            RegisteredTypes[t] = td;
            NameToType[name] = t;
        }

        [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
        public class FieldAttribute : System.Attribute
        {
            public readonly string Name;
            public readonly string DataType;

            public FieldAttribute(string name, string dataType = "String")
            {
                this.Name = name;
                this.DataType = dataType;
            }
        }

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
        public class MessageAttribute : System.Attribute
        {
            public readonly string Name;
            public readonly int DelaySeconds;
            public MessageAttribute(string name, int delaySeconds = 5)
            {
                Name = name;
                DelaySeconds = delaySeconds;
            }
        }

        [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
        public class BodyAttribute : System.Attribute { }
#endregion Reflection magic
    }
}
