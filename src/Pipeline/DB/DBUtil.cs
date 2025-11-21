using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;

namespace JPLOPS.Pipeline
{
    public class DBHashKeyAttribute : Attribute { }

    public class DBRangeKeyAttribute : Attribute { }

    public class DBUtil
    {
        public static string GetTableName(Type type)
        {
            return type.Name;
        }
        
        private static MemberInfo[] GetPublicFieldsAndProperties(Type type)
        {
            var flags = BindingFlags.Public | BindingFlags.Instance;
            var fields = type.GetFields(flags);
            var props = type.GetProperties(flags);
            return fields.Cast<MemberInfo>().Concat(props.Cast<MemberInfo>()).ToArray();
        }

        public class PropInfo
        {
            public MemberInfo Info;
            public Type Type;
            public string Name { get { return Info.Name; } }

            public PropInfo(MemberInfo info)
            {
                this.Info = info;
                this.Type = info is PropertyInfo ? (info as PropertyInfo).PropertyType : (info as FieldInfo).FieldType;
            }
        }

        public static Dictionary<string, PropInfo> GetDBPropMap(Type type)
        {
            Dictionary<string, PropInfo> ret = new Dictionary<string, PropInfo>();
            foreach (var info in GetPublicFieldsAndProperties(type))
            {
                ret[info.Name] = new PropInfo(info);
            }
            return ret;
        }

        public static bool IsHashKeyProp(MemberInfo prop)
        {
            return prop.GetCustomAttributes<DBHashKeyAttribute>()
                .Where(a => a.GetType() == typeof(DBHashKeyAttribute))
                .Any();
        }
        
        public static bool IsRangeKeyProp(MemberInfo prop)
        {
            return prop.GetCustomAttributes<DBRangeKeyAttribute>()
                .Where(a => a.GetType() == typeof(DBRangeKeyAttribute))
                .Any();
        }

        public static void GetNameAndKeys(Type type, out string tableName, out string hashKey, out string rangeKey)
        {
            tableName = GetTableName(type);
            hashKey = rangeKey = null;
            foreach (var member in GetPublicFieldsAndProperties(type))
            {
                var name = member.Name;
                if (IsHashKeyProp(member))
                {
                    hashKey = name;
                }
                else if (IsRangeKeyProp(member))
                {
                    rangeKey = name;
                }
            }
        }

        public static string GetMemberValueAsString(MemberInfo member, object obj)
        {
            if (member is FieldInfo)
            {
                var val = (member as FieldInfo).GetValue(obj);
                if (val != null)
                {
                    return val.ToString();
                }
            }
            else if (member is PropertyInfo)
            {
                var val = (member as PropertyInfo).GetValue(obj);
                if (val != null)
                {
                    return val.ToString();
                }
            }
            return  string.Empty;
        }

        public static void GetKeyValues(Object obj, out string hashValue, out string rangeValue)
        {
            hashValue = rangeValue = null;
            foreach (var member in GetPublicFieldsAndProperties(obj.GetType()))
            {
                if (IsHashKeyProp(member))
                {
                    hashValue = GetMemberValueAsString(member, obj).ToString();
                }
                else if (IsRangeKeyProp(member))
                {
                    rangeValue = GetMemberValueAsString(member, obj).ToString();
                }
            }
        }
    }
}
