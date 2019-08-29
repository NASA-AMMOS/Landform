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

        public int Count
        {
            get
            {
                return keyToNode.Count;
            }
        }

        public bool DiskBacked
        {
            get { return keyToFilename != null; }
        }

        private int capacity;
        private string tempdir;

        private Func<TKey, string> keyToFilename;
        private Action<string, TValue> save;
        private Func<string, TValue> load;

        private class Entry
        {
            public TKey key;
            public TValue value;

            public Entry(TKey key, TValue value)
            {
                this.key = key;
                this.value = value;
            }
        }
        private LinkedList<Entry> values;
        private ConcurrentDictionary<TKey, LinkedListNode<Entry>> keyToNode;

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
            values = new LinkedList<Entry>();
            keyToNode = new ConcurrentDictionary<TKey, LinkedListNode<Entry>>();
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
        /// Check if a key is cached in memory
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool ContainsKey(TKey key)
        {
            return keyToNode.ContainsKey(key);
        }

        /// <summary>
        /// Remove an entry from the cache.
        /// </summary>
        /// <returns>True if entry was present and succesfully removed, false otherwise</returns>
        public bool Remove(TKey key)
        {
            if (keyToNode.TryGetValue(key, out LinkedListNode<Entry> node))
            {
                SaveIfDiskBacked(key, node.Value.value);
                lock (values)
                {
                    values.Remove(node); //OK if already removed
                }
                return keyToNode.TryRemove(key, out var junk); //OK if already removed
            }
            else
            {
                return false;
            }
        }
        
        public TValue this[TKey key]
        {
            //returns null if key not found
            get
            {
                if (keyToNode.TryGetValue(key, out LinkedListNode<Entry> node))
                {
                    return node.Value.value;
                }
                else if (DiskBacked && File.Exists(Path.Combine(tempdir, keyToFilename(key))))
                {
                    var value = load(Path.Combine(tempdir, keyToFilename(key)));
                    this[key] = value;
                    return value;
                }
                else
                {
                    return default(TValue);
                }
            }

            set
            {
                lock (values)
                {
                    LinkedListNode<Entry> node = null;
                    if (keyToNode.TryGetValue(key, out node))
                    {
                        node.Value.value = value;
                        values.Remove(node);
                        values.AddFirst(node);
                    }
                    else
                    {
                        node = values.AddFirst(new Entry(key, value));
                        Trim();
                        keyToNode.AddOrUpdate(key, _ => node, (_, __) => node);
                    }
                }
            }
        }
        
        /// <summary>
        /// Trim cache to be no greater than Capacity elements.
        /// </summary>
        private void Trim()
        {
            lock (values)
            {
                while (values.Count > Capacity)
                {
                    var last = values.Last;
                    SaveIfDiskBacked(last.Value.key, last.Value.value);
                    keyToNode.TryRemove(last.Value.key, out var junk);
                    values.RemoveLast();
                }
            }
        }

        private void SaveIfDiskBacked(TKey key, TValue obj)
        {
            if (save != null)
            {
                save(Path.Combine(tempdir, keyToFilename(key)), obj);
            } 
        }
    }
}
