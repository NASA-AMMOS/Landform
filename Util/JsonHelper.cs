using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;


namespace JPLOPS.Util
{
    public class IgnorePropertiesResolver : DefaultContractResolver
    {
        private HashSet<string> ignore;

        /// <summary>
        /// skip certain properties for serialization
        /// each name should be of the form TypeName.PropertyName
        /// </summary>
        public IgnorePropertiesResolver(string[] names)
        {
            ignore = new HashSet<string>(names);
        }

        protected override JsonProperty CreateProperty(System.Reflection.MemberInfo mi, MemberSerialization ms)
        {
            JsonProperty prop = base.CreateProperty(mi, ms);
            string name = prop.DeclaringType.Name + "." + prop.PropertyName;
            prop.Ignored = ignore.Contains(name);
            return prop;
        }
    }

    public class JsonHelper
    {
        public static JsonSerializerSettings Settings(bool autoTypes = true, string[] ignoreProperties = null,
                                                      bool ignoreNulls = false)
        {
            var settings = new JsonSerializerSettings();
            if (autoTypes) settings.TypeNameHandling = TypeNameHandling.Auto;
            if (ignoreNulls) settings.NullValueHandling = NullValueHandling.Ignore;
            if (ignoreProperties != null) settings.ContractResolver = new IgnorePropertiesResolver(ignoreProperties);

            //serialize enums as their string equivalents instead of ints
            //the main reason is to reduce backwards compatibility problems if we add values to an enum
            //this also improves readability of the local database
            //but at the expense of increased disk usage
            //(enums that were previously serialized as ints are still accepted)
            settings.Converters.Add(new StringEnumConverter());

            return settings;
        }

        public static string ToJson(Object o, bool indent = false, bool autoTypes = true,
                                    string[] ignoreProperties = null, bool ignoreNulls = false)
        {
            Formatting formatting = indent ? Formatting.Indented : Formatting.None;
            return JsonConvert.SerializeObject(o, typeof(object), formatting,
                                               Settings(autoTypes, ignoreProperties, ignoreNulls));
        }

        public static object FromJson(string json, bool autoTypes = true, string[] ignoreProperties = null,
                                      bool ignoreNulls = false)
        {
            return JsonConvert.DeserializeObject(json, Settings(autoTypes, ignoreProperties, ignoreNulls));
        }

        public static T FromJson<T>(string json, bool autoTypes = true, string[] ignoreProperties = null,
                                    bool ignoreNulls = false)
        {
            return JsonConvert.DeserializeObject<T>(json, Settings(autoTypes, ignoreProperties, ignoreNulls));
        }

        public static object FromJson(string json, object obj, bool autoTypes = true, string[] ignoreProperties = null,
                                      bool ignoreNulls = false)
        {
            JsonConvert.PopulateObject(json, obj, Settings(autoTypes, ignoreProperties, ignoreNulls));
            return obj;
        }
    }
}
