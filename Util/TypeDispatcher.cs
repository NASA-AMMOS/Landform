using System;
using System.Collections;
using System.Collections.Generic;

namespace OPS.Util
{
    /// <summary>
    /// based on https://stackoverflow.com/questions/7252186/switch-case-on-type-c-sharp/7301514#7301514
    /// </summary>
    public class TypeDispatcher
    {
        public Action<Type, object> Unhandled = (t, x) => throw new System.Exception("unhandled type: " + t);

        private Dictionary<Type, Action<object>> handlers = new Dictionary<Type, Action<object>>();

        public TypeDispatcher Case<T>(Action<T> action)
        {
            handlers.Add(typeof(T), (x) => action((T)x));
            return this;
        } 
        
        public bool Handle(object x)
        {
            var t = x.GetType();
            if (handlers.ContainsKey(t))
            {
                handlers[x.GetType()](x);
                return true;
            }
            else
            {
                if (Unhandled != null) Unhandled(t, x);
                return false;
            }
        }
    }
}
