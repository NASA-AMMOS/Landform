using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace JPLOPS.Util
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

        private int hits, misses, bumped, diskHits, diskBumped;

        public string GetStats()
        {
            int total = hits + misses;
            int hitRate = (int)(100 * ((float)hits) / (total > 0 ? total : 1));
            var stats = string.Format("{0} hits ({1}%), {2} misses, {3} bumped",
                                      Fmt.KMG(hits), hitRate, Fmt.KMG(misses), Fmt.KMG(bumped));
            if (DiskBacked)
            {
                stats += string.Format(", {0} disk hits, {1} disk bumped", Fmt.KMG(diskHits), Fmt.KMG(diskBumped));
            }
            return stats;
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

        public void Clear()
        {
            lock (values)
            {
                values.Clear();
                keyToNode.Clear();
            }
        }

        public TValue this[TKey key]
        {
            get //returns null if key not found
            {
                if (keyToNode.TryGetValue(key, out LinkedListNode<Entry> node))
                {
                    Interlocked.Increment(ref hits);
                    var value = node.Value.value;
                    this[key] = value; //mark as most recently used
                    return value;
                }
                else if (DiskBacked && File.Exists(Path.Combine(tempdir, keyToFilename(key))))
                {
                    Interlocked.Increment(ref misses);
                    Interlocked.Increment(ref diskHits);
                    var value = load(Path.Combine(tempdir, keyToFilename(key)));
                    this[key] = value; //add to cache and mark as most recently used
                    return value;
                }
                else
                {
                    Interlocked.Increment(ref misses);
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
                    else if (capacity > 0)
                    {
                        node = values.AddFirst(new Entry(key, value));
                        keyToNode.AddOrUpdate(key, _ => node, (_, __) => node);
                        Trim();
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
                    Interlocked.Increment(ref bumped);
                }
            }
        }

        private void SaveIfDiskBacked(TKey key, TValue obj)
        {
            if (save != null)
            {
                Interlocked.Increment(ref diskBumped);
                save(Path.Combine(tempdir, keyToFilename(key)), obj);
            } 
        }
    }
}
