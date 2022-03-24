using System;
using System.Collections.Generic;

namespace JPLOPS.Util
{
    //https://stackoverflow.com/questions/2081559/is-there-an-equivalent-for-java-weakhashmap-class-in-c
    //https://blogs.msdn.microsoft.com/jaredpar/2009/03/03/building-a-weakreference-hashtable
    public class WeakDictionary<TKey, TValue>
    {
        private Dictionary<TKey, WeakReference> dict;

        public WeakDictionary() : this(EqualityComparer<TKey>.Default)
        {
        }

        public WeakDictionary(IEqualityComparer<TKey> comparer)
        {
            dict = new Dictionary<TKey, WeakReference>(comparer);
        }

        public void Add(TKey key, TValue value)
        {
            dict.Add(key, new WeakReference(value));
        }

        public void Put(TKey key, TValue value)
        {
            dict[key] = new WeakReference(value);
        }

        public bool Remove(TKey key)
        {
            return dict.Remove(key);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            value = default(TValue);
            WeakReference weakRef;
            if (!dict.TryGetValue(key, out weakRef))
            {
                return false;
            }

            var target = weakRef.Target;
            if (target == null)
            {
                return false;
            }

            value = (TValue)target;
            return true;
        }
    }
}
