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

        private Action<TValue, string> _save;
        private Func<TKey, string> _keyToString;

        public bool DiskBacked
        {
            get { return _save != null && _keyToString != null; }
        }

        private void flush(TKey key, TValue obj)
        {
            if (_save != null)
            {
                _save(obj, Path.Combine(_tempdir, _keyToString(key)));
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
            get
            {
                lock (Values)
                {
                    return Values.Count;
                }
            }
        }

        public LRUCache(int capacity, Action<TValue, string> flush = null, Func<TKey, string> keyToString = null)
        {
            this._save = flush;
            this._keyToString = keyToString;
            if(flush == null && keyToString != null || flush != null && keyToString == null)
            {
                throw new Exception("LRU Cache needs both TKey to filename and save TValue functions to back to disk. Set both null to use in memory cache.");
            }
            if(flush != null && keyToString != null)
            {
                _tempdir = TemporaryFile.GetTempSubdir();
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
                TemporaryFile.DeleteTempDirectory();
            }
        }

        /// <summary>
        /// Check if a key exists in memory
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool ContainsKey(TKey key)
        {
            return KeyToNode.ContainsKey(key);
        }

        /// <summary>
        /// Check if a key exists on disk
        /// </summary>
        /// <returns></returns>
        public bool ContainsKeyOnDisk(TKey key)
        {
            return DiskBacked && File.Exists(Path.Combine(_tempdir, _keyToString(key)));
        }

        /// <summary>
        /// Load a key value pair back into cache memory if it has been flushed to disk.
        /// /// </summary>
        /// <param name="key"></param>
        /// <param name="load"></param>
        public void EnsureLoaded(TKey key, Func<string, TValue> load)
        {
            if(!ContainsKeyOnDisk(key))
            {
                throw new KeyNotFoundException();
            }
            if (!ContainsKey(key))
            {
                TValue val = load(_keyToString(key));
                Add(key, val);
            }
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
