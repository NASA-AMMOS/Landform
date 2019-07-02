using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Util
{
    public class LRUCache<TKey, TValue>
    {
        /// <summary>
        /// Maximum number of entries in the cache 
        /// </summary>
        public int Capacity
        {
            get { return _capacity; }
            set
            {
                _capacity = value;
                if (Count > value)
                {
                    Trim();
                }
            }
        }
        private int _capacity;
        private string _tempdir;

        public bool DiskBacked
        {
            get { return _save != null; }
        }

        private Action<TValue, TKey, string> _save;
        private void flush(TKey key, TValue obj)
        {
            if (_save != null)
            {
                _save(obj, key, _tempdir);
            } else
            {
                throw new Exception("LRUCache failed to flush.");
            }
        }

        /// <summary>
        /// Number of entries currently in the cache
        /// </summary>
        public int Count
        {
            get { return Values.Count; }
        }

        public LRUCache(int capacity, string workingDir="", Action<TValue, TKey, string> saveFunc = null)
        {
            this._save = saveFunc;
            if(saveFunc != null)
            {
                _tempdir = workingDir + "/tmp" + DateTime.Now.ToString("hmmsstt") + "/";
                Directory.CreateDirectory(_tempdir);
            }
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException("capacity", capacity, "Capacity must be >= 1");
            }

            _capacity = capacity;
            Values = new LinkedList<Entry>();
            KeyToNode = new ConcurrentDictionary<TKey, LinkedListNode<Entry>>();
        }

        /// <summary>
        /// Delete temporary files if needed
        /// </summary>
        ~LRUCache ()
        {
            if (DiskBacked)
            {
                Directory.Delete(_tempdir, true);
            }
        }

        public bool ContainsKey(TKey key)
        {
            return KeyToNode.ContainsKey(key);
        }

        /// <summary>
        /// Add an entry to the cache.
        /// </summary>
        public void Add(TKey key, TValue value)
        {
            // Add entry to cache
            LinkedListNode<Entry> node;
            lock (Values)
            {
                node = Values.AddFirst(new Entry(key, value));
            }
            KeyToNode[key] = node;

            Trim();
        }

        /// <summary>
        /// Remove an entry from the cache.
        /// </summary>
        /// <returns>True if entry was present and succesfully removed, false otherwise</returns>
        public bool Remove(TKey key)
        {
            if (!ContainsKey(key)) return false;

            var node = KeyToNode[key];
            flush(key, node.Value.Value);
            lock (Values)
            {
                Values.Remove(node);
            }
            return KeyToNode.TryRemove(key, out var junk);
        }
        
        public TValue this[TKey key]
        {
            get
            {
                if (!ContainsKey(key))
                {
                    throw new KeyNotFoundException();
                }
                return KeyToNode[key].Value.Value;
            }
            set
            {
                if (ContainsKey(key))
                {
                    var node = KeyToNode[key];
                    node.Value.Value = value;
                    TouchNode(node);
                }
                else
                {
                    Add(key, value);
                }
            }
        }
        
        private class Entry
        {
            public TKey Key;
            public TValue Value;

            public Entry(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }
        }
        private LinkedList<Entry> Values;
        private ConcurrentDictionary<TKey, LinkedListNode<Entry>> KeyToNode;

        /// <summary>
        /// Move an entry to the front of the cache.
        /// </summary>
        /// <param name="node"></param>
        private void TouchNode(LinkedListNode<Entry> node)
        {
            lock (Values)
            {
                Values.Remove(node);
                Values.AddFirst(node);
            }
        }

        /// <summary>
        /// Trim cache to be no greater than Capacity elements.
        /// </summary>
        private void Trim()
        {
            lock (Values)
            {
                while (Values.Count > Capacity)
                {
                    var last = Values.Last;
                    flush(last.Value.Key, last.Value.Value);
                    if (!KeyToNode.TryRemove(last.Value.Key, out var junk))
                    {
                        throw new Exception("it broke");
                    }
                    Values.RemoveLast();
                }
            }
        }
    }
}
