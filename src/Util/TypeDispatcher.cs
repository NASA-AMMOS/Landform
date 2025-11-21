using System;
using System.Collections.Generic;

namespace JPLOPS.Util
{
    /// <summary>
    /// based on https://stackoverflow.com/questions/7252186/switch-case-on-type-c-sharp/7301514#7301514
    /// </summary>
    public class TypeDispatcher
    {
        public Action<Type, object> Unhandled = (t, x) => throw new Exception("unhandled type: " + t);

        private Dictionary<Type, Action<object>> handlers = new Dictionary<Type, Action<object>>();

        public TypeDispatcher Case<T>(Action<T> action)
        {
            handlers.Add(typeof(T), (x) => action(x == null ? default(T) : (T)x));
            return this;
        } 
        
        public bool Handle(object x)
        {
            return Handle(x.GetType(), x);
        }

        public bool Handle(Type t, object x)
        {
            if (t.IsEnum)
            {
                t = typeof(Enum);
            }
            if (handlers.ContainsKey(t))
            {
                handlers[t](x);
                return true;
            }
            else
            {
                if (Unhandled != null)
                {
                    Unhandled(t, x);
                }
                return false;
            }
        }

        public bool Handle(Type t)
        {
            return Handle(t, null);
        }
    }
}
