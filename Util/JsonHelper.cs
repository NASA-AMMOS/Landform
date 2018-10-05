using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace OPS.Util
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
        public static string ToJson(Object o, bool indent = false, bool autoTypes = true,
                                    string[] ignoreProperties = null)
        {
            var settings = new JsonSerializerSettings();
            if (autoTypes) settings.TypeNameHandling = TypeNameHandling.Auto;
            if (ignoreProperties != null) settings.ContractResolver = new IgnorePropertiesResolver(ignoreProperties);
            Formatting formatting = indent ? Formatting.Indented : Formatting.None;
            return JsonConvert.SerializeObject(o, typeof(object), formatting, settings);
        }

        public static object FromJson(string json, bool autoTypes = true, string[] ignoreProperties = null)
        {
            var settings = new JsonSerializerSettings();
            if (autoTypes) settings.TypeNameHandling = TypeNameHandling.Auto;
            if (ignoreProperties != null) settings.ContractResolver = new IgnorePropertiesResolver(ignoreProperties);
            return JsonConvert.DeserializeObject(json, settings);
        }

    }
}
