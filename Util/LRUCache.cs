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
            get { return capacity; }
            set
            {
                capacity = value;
                if (Count > value)
                {
                    Trim();
                }
            }
        }
        private int capacity;
        private string tempdir;

        private Func<TKey, string> keyToFilename;
        private Action<string, TValue> save;
        private Func<string, TValue> load;

        public bool DiskBacked
        {
            get { return keyToFilename != null; }
        }

        private void SaveIfDiskBacked(TKey key, TValue obj)
        {
            if (save != null)
            {
                save(Path.Combine(tempdir, keyToFilename(key)), obj);
            } 
        }

        private TValue LoadIfDiskBacked(TKey key)
        {
            if (load != null)
            {
                return load(Path.Combine(tempdir, keyToFilename(key)));
            }
            else
            {
                throw new InvalidOperationException();
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

        /// <summary>
        /// creates an in-memory LRU cache
        /// </summary>
        public LRUCache(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException("capacity", capacity, "capacity must be >= 1");
            }
            this.capacity = capacity;
            Values = new LinkedList<Entry>();
            KeyToNode = new ConcurrentDictionary<TKey, LinkedListNode<Entry>>();
        }

        /// <summary>
        /// creates a disk-backed LRU cache
        /// </summary>
        public LRUCache(int capacity, Func<TKey, string> keyToFilename, Action<string, TValue> save,
                        Func<string, TValue> load)
            : this(capacity)
        {
            this.keyToFilename = keyToFilename;
            this.save = save;
            this.load = load;
            tempdir = TemporaryFile.GetTempSubdir();
        }

        /// <summary>
        /// Delete temporary files if needed
        /// </summary>
        ~LRUCache ()
        {
            if (DiskBacked)
            {
                TemporaryFile.DeleteTempSubdir(tempdir);
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
            return DiskBacked && File.Exists(Path.Combine(tempdir, keyToFilename(key)));
        }

        /// <summary>
        /// Load a key value pair back into cache memory if it has been flushed to disk.
        /// </summary>
        /// <param name="key"></param>
        public void EnsureLoaded(TKey key)
        {
            if (!ContainsKey(key))
            {
                if (ContainsKeyOnDisk(key))
                {
                    Add(key, LoadIfDiskBacked(key));
                }
                else
                {
                    throw new KeyNotFoundException();
                }
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

            SaveIfDiskBacked(key, node.Value.Value);

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

                    SaveIfDiskBacked(last.Value.Key, last.Value.Value);

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
