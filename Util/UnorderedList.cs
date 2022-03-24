using System.Collections.Generic;

namespace JPLOPS.Util
{
    /// <summary>
    /// List that has no order but allows for constant-time removal and random access
    /// </summary>
    public class UnorderedList<T>
    {
        // Ordered list which the data is stored in
        private List<T> list;
        
        // Number of elements currently held in the unordered list
        public int Count { get; private set; }

        public UnorderedList()
        {
            list = new List<T>();
        }

        public UnorderedList(int initialCapacity)
        {
            list = new List<T>(initialCapacity);
        }

        /// <summary>
        /// Adds an element to the list, which increases the element count
        /// </summary>
        /// <param name="item">Element to add to the unordered list</param>
        public void Add(T item)
        {
            list.Add(item);
            Count++;
        }
        
        /// <summary>
        /// Removes an element from the list at a given index by placing the last element in its place and decreasing
        /// the count
        /// </summary>
        /// <param name="index">Index of the element to be removed</param>
        public void Remove(int index)
        {
            Count--;
            list[index] = list[Count];
        }
        
        /// <summary>
        /// Empties the list by setting its count to 0
        /// </summary>
        public void Empty()
        {
            Count = 0;
        }
        
        /// <summary>
        /// Enables reading and writing of the element at a given index
        /// </summary>
        /// <param name="index">Index of the unordered list to read or write at</param>
        /// <returns>The value if being read, nothing if being written</returns>
        public T this[int index]
        {
            get
            {
                return list[index];
            }
            set
            {
                list[index] = value;
            }
        }
    }
}
