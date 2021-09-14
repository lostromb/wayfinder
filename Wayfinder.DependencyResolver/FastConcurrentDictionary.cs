using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Wayfinder.DependencyResolver
{
    /// <summary>
    /// Implements a concurrent dictionary with "loose" concurrency rules. Specifically,
    /// any operation (including enumeration) can be performed without external locking, however,
    /// results of conflicting operations are approximate. For example, if you Add() the same key
    /// twice with different values concurrently, one value will be chosen arbitrarily. Or if you access
    /// Count while an insertion is taking place, the count may not be fully accurate.
    /// </summary>
    /// <typeparam name="K">The type of keys to be stored in this dictionary.</typeparam>
    /// <typeparam name="V">The type of values to be stored in this dictionary.</typeparam>
    public class FastConcurrentDictionary<K, V> : IDictionary<K, V>
    {
        // Recycle objects to reduce memory allocations
        private static readonly ConcurrentQueue<HashTableLinkedListNode> RECLAIMED_NODES = new ConcurrentQueue<HashTableLinkedListNode>();

        private const int DEFAULT_INITIAL_CAPACITY = 64;
        private const int NUM_LOCKS = 16;
        private const int MAX_TABLE_SIZE = 0x3FF0000; // A little less than half of int.MaxValue. Above this amount, we stop allocating new tables

        private readonly IEqualityComparer<K> _keyComparer;
        private readonly IEqualityComparer<V> _valueComparer;
        private readonly object[] _binLocks;
        private HashTableLinkedListNode[] _bins;
        private volatile int _numItemsInDictionary;

        /// <summary>
        /// Creates a fast concurrent dictionary with a default initial capacity
        /// </summary>
        public FastConcurrentDictionary() : this(DEFAULT_INITIAL_CAPACITY, EqualityComparer<K>.Default)
        {
        }

        public FastConcurrentDictionary(int initialCapacity) : this(initialCapacity, EqualityComparer<K>.Default)
        {
        }

        /// <summary>
        /// Creates a fast concurrent dictionary with the specified initial capacity
        /// </summary>
        /// <param name="initialCapacity"></param>
        /// <param name="keyComparer"></param>
        public FastConcurrentDictionary(int initialCapacity, IEqualityComparer<K> keyComparer)
        {
            if (initialCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            if (keyComparer == null)
            {
                throw new ArgumentNullException(nameof(keyComparer));
            }

            _keyComparer = keyComparer;
            _valueComparer = EqualityComparer<V>.Default;

            if (initialCapacity > MAX_TABLE_SIZE)
            {
                initialCapacity = MAX_TABLE_SIZE;
            }

            _binLocks = new object[NUM_LOCKS];
            for (int c = 0; c < NUM_LOCKS; c++)
            {
                _binLocks[c] = new object();
            }

            _numItemsInDictionary = 0;
            _bins = new HashTableLinkedListNode[initialCapacity];
        }

        /// <summary>
        /// Gets or sets a single item in the dictionary.
        /// <listheader>BEHAVIOR:</listheader>
        /// <list type="bullet">
        /// <item>Setting a value will not throw an exception.</item>
        /// <item>Getting a value that does not exist will throw a <see cref="KeyNotFoundException"/>.</item>
        /// <item>If you set a value and the key already exists, the value will be overwritten if this is the most recent concurrent operation.</item>
        /// </list>
        /// </summary>
        /// <param name="key">The key to access</param>
        /// <returns>The value stored at that key</returns>
        public V this[K key]
        {
            get
            {
                return GetInternal(key);
            }
            set
            {
                Add(new KeyValuePair<K, V>(key, value));
            }
        }

        /// <summary>
        /// Gets a copy of all of the keys currently in the collection. This method is less efficient that simply enumerating the dictionary.
        /// You should rarely have to use this since you can safely modify this collection during enumeration.
        /// </summary>
        public ICollection<K> Keys
        {
            get
            {
                // OPT this method is slow
                IList<K> returnVal = new List<K>();
                foreach (var kvp in this)
                {
                    returnVal.Add(kvp.Key);
                }

                return returnVal;
            }
        }

        /// <summary>
        /// Gets a copy of all of the values currently in the collection. This method is less efficient that simply enumerating the dictionary.
        /// You should rarely have to use this since you can safely modify this collection during enumeration.
        /// </summary>
        public ICollection<V> Values
        {
            get
            {
                // OPT this method is slow
                IList<V> returnVal = new List<V>();
                foreach (var kvp in this)
                {
                    returnVal.Add(kvp.Value);
                }

                return returnVal;
            }
        }

        /// <summary>
        /// Returns the approximate count of the number of items in the dictionary.
        /// </summary>
        public int Count => _numItemsInDictionary;

        public bool IsReadOnly => false;

        /// <summary>
        /// Adds an item to the dictionary. If the key already exists, the value will be overwritten concurrently.
        /// </summary>
        /// <param name="key">The key to add.</param>
        /// <param name="value">The value to be added.</param>
        public void Add(K key, V value)
        {
            Add(new KeyValuePair<K, V>(key, value));
        }

        /// <summary>
        /// Adds an item to the dictionary. If the key already exists, the value will be overwritten concurrently.
        /// </summary>
        /// <param name="item">The item to be added.</param>
        public void Add(KeyValuePair<K, V> item)
        {
            ExpandTableIfNeeded();

            // Copy a local reference of the table in case another thread tries to resize it while we are accessing
            HashTableLinkedListNode[] bins = _bins;
            uint keyHash = (uint)item.Key.GetHashCode();
            uint bin = keyHash % (uint)bins.Length;
            uint binLock = bin % NUM_LOCKS;

            // Acquire the lock to the bin we want
            Monitor.Enter(_binLocks[binLock]);

            // Detect if the table was resized while we were getting the lock
            while (_bins != bins)
            {
                // If so, we need to recalculate the bin # based on the new table size and then try getting the lock again
                Monitor.Exit(_binLocks[binLock]);
                bins = _bins;
                bin = keyHash % (uint)bins.Length;
                binLock = bin % NUM_LOCKS;
                Monitor.Enter(_binLocks[binLock]);
            }

            try
            {
                if (bins[bin] == null)
                {
                    // Bin is empty; fill bin with new entry
                    bins[bin] = AllocateNode(item, keyHash);
                    Interlocked.Increment(ref _numItemsInDictionary);
                }
                else
                {
                    // Bin is full; see if a value already exists
                    HashTableLinkedListNode iter = bins[bin];
                    HashTableLinkedListNode endOfBin = iter;
                    while (iter != null)
                    {
                        // Does an entry already exist with this same key?
                        if (_keyComparer.Equals(item.Key, iter.Kvp.Key))
                        {
                            // Update the value of the existing item
                            iter.Kvp = item;
                            return;
                        }

                        iter = iter.Next;
                        if (iter != null)
                        {
                            endOfBin = iter;
                        }
                    }

                    // Key was not found after iterating the bin. Append a new entry to the end of the bin
                    endOfBin.Next = AllocateNode(item, keyHash);
                    Interlocked.Increment(ref _numItemsInDictionary);
                }
            }
            finally
            {
                Monitor.Exit(_binLocks[binLock]);
            }
        }

        /// <summary>
        /// Safely clears all values from the dictionary.
        /// </summary>
        public void Clear()
        {
            // Acquire all locks
            for (int binLock = 0; binLock < NUM_LOCKS; binLock++)
            {
                Monitor.Enter(_binLocks[binLock]);
            }

            try
            {
                // Iterate through all bins and clean them
                for (int bin = 0; bin < _bins.Length; bin++)
                {
                    _bins[bin] = null;
                }

                _numItemsInDictionary = 0;
            }
            finally
            {
                // Release all locks
                for (int binLock = 0; binLock < NUM_LOCKS; binLock++)
                {
                    Monitor.Exit(_binLocks[binLock]);
                }
            }
        }

        /// <summary>
        /// Determines if the specified key + value exists in the dictionary.
        /// </summary>
        /// <param name="item">The item to check for.</param>
        /// <returns>True if the specified key+value is in the dictionary.</returns>
        public bool Contains(KeyValuePair<K, V> item)
        {
            // Copy a local reference of the table in case another thread tries to resize it while we are accessing
            HashTableLinkedListNode[] bins = _bins;
            uint keyHash = (uint)item.Key.GetHashCode();
            uint bin = keyHash % (uint)bins.Length;
            uint binLock = bin % NUM_LOCKS;

            // Acquire the lock to the bin we want
            Monitor.Enter(_binLocks[binLock]);

            // Detect if the table was resized while we were getting the lock
            while (_bins != bins)
            {
                // If so, we need to recalculate the bin # based on the new table size and then try getting the lock again
                Monitor.Exit(_binLocks[binLock]);
                bins = _bins;
                bin = keyHash % (uint)bins.Length;
                binLock = bin % NUM_LOCKS;
                Monitor.Enter(_binLocks[binLock]);
            }

            try
            {
                if (bins[bin] == null)
                {
                    // Bin is empty.
                    return false;
                }
                else
                {
                    // Bin is full; see if a value already exists
                    HashTableLinkedListNode iter = bins[bin];
                    while (iter != null)
                    {
                        if (_keyComparer.Equals(item.Key, iter.Kvp.Key) &&
                            _valueComparer.Equals(item.Value, iter.Kvp.Value))
                        {
                            // Found it!
                            return true;
                        }

                        iter = iter.Next;
                    }

                    return false;
                }
            }
            finally
            {
                Monitor.Exit(_binLocks[binLock]);
            }
        }

        /// <summary>
        /// Returns true if the dictionary contains a value with the specified key.
        /// </summary>
        /// <param name="key">The key to check for.</param>
        /// <returns>True if the specified key is in the dictionary.</returns>
        public bool ContainsKey(K key)
        {
            // Copy a local reference of the table in case another thread tries to resize it while we are accessing
            HashTableLinkedListNode[] bins = _bins;
            uint keyHash = (uint)key.GetHashCode();
            uint bin = keyHash % (uint)bins.Length;
            uint binLock = bin % NUM_LOCKS;

            // Acquire the lock to the bin we want
            Monitor.Enter(_binLocks[binLock]);

            // Detect if the table was resized while we were getting the lock
            while (_bins != bins)
            {
                // If so, we need to recalculate the bin # based on the new table size and then try getting the lock again
                Monitor.Exit(_binLocks[binLock]);
                bins = _bins;
                bin = keyHash % (uint)bins.Length;
                binLock = bin % NUM_LOCKS;
                Monitor.Enter(_binLocks[binLock]);
            }

            try
            {
                if (bins[bin] == null)
                {
                    // Bin is empty.
                    return false;
                }
                else
                {
                    // Bin is full; see if a value already exists
                    HashTableLinkedListNode iter = bins[bin];
                    while (iter != null)
                    {
                        if (_keyComparer.Equals(key, iter.Kvp.Key))
                        {
                            // Found it!
                            return true;
                        }

                        iter = iter.Next;
                    }

                    return false;
                }
            }
            finally
            {
                Monitor.Exit(_binLocks[binLock]);
            }
        }

        /// <summary>
        /// Copies all key/values from this dictionary to another collection.
        /// The copy may be incomplete if the dictionary is being concurrently modified.
        /// </summary>
        /// <param name="array"></param>
        /// <param name="arrayIndex"></param>
        public void CopyTo(KeyValuePair<K, V>[] array, int arrayIndex)
        {
            // Enumerate and copy
            using (IEnumerator<KeyValuePair<K, V>> enumerator = GetEnumerator())
            {
                int idx = 0;
                while (enumerator.MoveNext())
                {
                    array[arrayIndex + idx] = enumerator.Current;
                    idx++;
                }
            }
        }

        /// <summary>
        /// Enumerates the key-value pairs in this dictionary.
        /// The only guarantee provided by enumeration while other threads are modifying the collection are:
        /// <list type="number">
        /// <item>The enumerator will not throw an exception</item>
        /// <item>The enumerator will not enumerate the same key more than once</item>
        /// </list>
        /// If no concurrent access is going on then you can make the same assumptions as with a regular dictionary.
        /// </summary>
        /// <returns>An enumerator for this dictionary</returns>
        public IEnumerator<KeyValuePair<K, V>> GetEnumerator()
        {
            return new FastConcurrentDictionaryEnumerator(this);
        }

        /// <summary>
        /// Returns an enumerator that begins in a random point of the dictionary and enumerates all values.
        /// </summary>
        /// <returns></returns>
        public IEnumerator<KeyValuePair<K, V>> GetRandomEnumerator(Random rand)
        {
            return new FastConcurrentDictionaryEnumerator(this, rand);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new FastConcurrentDictionaryEnumerator(this);
        }

        /// <summary>
        /// Attempts to remove a value with the specified key from the dictionary.
        /// </summary>
        /// <param name="key">The key to try and remove.</param>
        /// <returns>True if a value was removed.</returns>
        public bool Remove(K key)
        {
            // Copy a local reference of the table in case another thread tries to resize it while we are accessing
            HashTableLinkedListNode[] bins = _bins;
            uint keyHash = (uint)key.GetHashCode();
            uint bin = keyHash % (uint)bins.Length;
            uint binLock = bin % NUM_LOCKS;

            // Acquire the lock to the bin we want
            Monitor.Enter(_binLocks[binLock]);

            // Detect if the table was resized while we were getting the lock
            while (_bins != bins)
            {
                // If so, we need to recalculate the bin # based on the new table size and then try getting the lock again
                Monitor.Exit(_binLocks[binLock]);
                bins = _bins;
                bin = keyHash % (uint)bins.Length;
                binLock = bin % NUM_LOCKS;
                Monitor.Enter(_binLocks[binLock]);
            }

            try
            {
                if (bins[bin] == null)
                {
                    // Bin is empty.
                    return false;
                }
                else
                {
                    // Bin is full; see if a value already exists
                    HashTableLinkedListNode iter = bins[bin];
                    HashTableLinkedListNode prevNode = null;
                    while (iter != null)
                    {
                        if (_keyComparer.Equals(key, iter.Kvp.Key))
                        {
                            // Found it! Update the linked list
                            if (prevNode == null)
                            {
                                bins[bin] = iter.Next;
                            }
                            else
                            {
                                prevNode.Next = iter.Next;
                            }

                            iter.Next = null;
                            DeallocateNode(iter);
                            Interlocked.Decrement(ref _numItemsInDictionary);
                            return true;
                        }

                        prevNode = iter;
                        iter = iter.Next;
                    }

                    return false;
                }
            }
            finally
            {
                Monitor.Exit(_binLocks[binLock]);
            }
        }

        public bool Remove(KeyValuePair<K, V> item)
        {
            // Copy a local reference of the table in case another thread tries to resize it while we are accessing
            HashTableLinkedListNode[] bins = _bins;
            uint keyHash = (uint)item.Key.GetHashCode();
            uint bin = keyHash % (uint)bins.Length;
            uint binLock = bin % NUM_LOCKS;

            // Acquire the lock to the bin we want
            Monitor.Enter(_binLocks[binLock]);

            // Detect if the table was resized while we were getting the lock
            while (_bins != bins)
            {
                // If so, we need to recalculate the bin # based on the new table size and then try getting the lock again
                Monitor.Exit(_binLocks[binLock]);
                bins = _bins;
                bin = keyHash % (uint)bins.Length;
                binLock = bin % NUM_LOCKS;
                Monitor.Enter(_binLocks[binLock]);
            }

            try
            {
                if (bins[bin] == null)
                {
                    // Bin is empty.
                    return false;
                }
                else
                {
                    // Bin is full; see if a value already exists
                    HashTableLinkedListNode iter = bins[bin];
                    HashTableLinkedListNode prevNode = null;
                    while (iter != null)
                    {
                        if (_keyComparer.Equals(item.Key, iter.Kvp.Key) &&
                            _valueComparer.Equals(item.Value, iter.Kvp.Value))
                        {
                            // Found it! Update the linked list
                            if (prevNode == null)
                            {
                                bins[bin] = iter.Next;
                            }
                            else
                            {
                                prevNode.Next = iter.Next;
                            }

                            iter.Next = null;
                            DeallocateNode(iter);
                            Interlocked.Decrement(ref _numItemsInDictionary);
                            return true;
                        }

                        prevNode = iter;
                        iter = iter.Next;
                    }

                    return false;
                }
            }
            finally
            {
                Monitor.Exit(_binLocks[binLock]);
            }
        }

        /// <summary>
        /// Attempts to remove a value with the specified key from the dictionary. If it is found, the value
        /// is removed from the table and put into the out parameter returnVal.
        /// </summary>
        /// <param name="key">The key to try and remove.</param>
        /// <param name="returnVal">The value removed from the table, if found.</param>
        /// <returns>True if a value was removed.</returns>
        public bool TryGetValueAndRemove(K key, out V returnVal)
        {
            // Copy a local reference of the table in case another thread tries to resize it while we are accessing
            HashTableLinkedListNode[] bins = _bins;
            uint keyHash = (uint)key.GetHashCode();
            uint bin = keyHash % (uint)bins.Length;
            uint binLock = bin % NUM_LOCKS;

            // Acquire the lock to the bin we want
            Monitor.Enter(_binLocks[binLock]);

            // Detect if the table was resized while we were getting the lock
            while (_bins != bins)
            {
                // If so, we need to recalculate the bin # based on the new table size and then try getting the lock again
                Monitor.Exit(_binLocks[binLock]);
                bins = _bins;
                bin = keyHash % (uint)bins.Length;
                binLock = bin % NUM_LOCKS;
                Monitor.Enter(_binLocks[binLock]);
            }

            try
            {
                if (bins[bin] == null)
                {
                    // Bin is empty.
                    returnVal = default(V);
                    return false;
                }
                else
                {
                    // Bin is full; see if a value already exists
                    HashTableLinkedListNode iter = bins[bin];
                    HashTableLinkedListNode prevNode = null;
                    while (iter != null)
                    {
                        if (_keyComparer.Equals(key, iter.Kvp.Key))
                        {
                            // Found it! Update the linked list
                            if (prevNode == null)
                            {
                                bins[bin] = iter.Next;
                            }
                            else
                            {
                                prevNode.Next = iter.Next;
                            }

                            iter.Next = null;
                            DeallocateNode(iter);
                            Interlocked.Decrement(ref _numItemsInDictionary);
                            returnVal = iter.Kvp.Value;
                            return true;
                        }

                        prevNode = iter;
                        iter = iter.Next;
                    }

                    returnVal = default(V);
                    return false;
                }
            }
            finally
            {
                Monitor.Exit(_binLocks[binLock]);
            }
        }

        /// <summary>
        /// Attempts to fetch a value with the specified key from the dictionary.
        /// </summary>
        /// <param name="key">The key to check for.</param>
        /// <param name="value">If found, the value that is retrieved.</param>
        /// <returns>True if the retrieve succeeded.</returns>
        public bool TryGetValue(K key, out V value)
        {
            // Copy a local reference of the table in case another thread tries to resize it while we are accessing
            HashTableLinkedListNode[] bins = _bins;
            uint keyHash = (uint)key.GetHashCode();
            uint bin = keyHash % (uint)bins.Length;
            uint binLock = bin % NUM_LOCKS;

            // Acquire the lock to the bin we want
            Monitor.Enter(_binLocks[binLock]);

            // Detect if the table was resized while we were getting the lock
            while (_bins != bins)
            {
                // If so, we need to recalculate the bin # based on the new table size and then try getting the lock again
                Monitor.Exit(_binLocks[binLock]);
                bins = _bins;
                bin = keyHash % (uint)bins.Length;
                binLock = bin % NUM_LOCKS;
                Monitor.Enter(_binLocks[binLock]);
            }

            try
            {
                if (bins[bin] == null)
                {
                    value = default(V);
                    return false;
                }
                else
                {
                    // Bin is full; see if a value already exists
                    HashTableLinkedListNode iter = bins[bin];
                    while (iter != null)
                    {
                        if (_keyComparer.Equals(key, iter.Kvp.Key))
                        {
                            // Found it!
                            value = iter.Kvp.Value;
                            return true;
                        }

                        iter = iter.Next;
                    }

                    value = default(V);
                    return false;
                }
            }
            finally
            {
                Monitor.Exit(_binLocks[binLock]);
            }
        }

        /// <summary>
        /// Performs an atomic TryGetValue + SetValue operation on the dictionary.
        /// This method behaves the same as TryGetValue, in that it attempts to retrieve a value from the dictionary
        /// and returns true if the value already exists. HOWEVER, If the value is NOT found in the dictionary,
        /// this method will insert a new value atomically, set the out parameter to be the new value, and return FALSE
        /// to indicate that the value was not originally in the dictionary.
        /// </summary>
        /// <param name="key">The key to attempt to fetch.</param>
        /// <param name="outputValue">The value retrieved from the dictionary.</param>
        /// <param name="valueIfNotFound">If the given key is not already in the dictionary, this value will be atomically inserted into the dictionary.</param>
        /// <returns>True if the key was already present in the dictionary.</returns>
        public bool TryGetValueOrSet(K key, out V outputValue, V valueIfNotFound)
        {
            // Copy a local reference of the table in case another thread tries to resize it while we are accessing
            HashTableLinkedListNode[] bins = _bins;
            uint keyHash = (uint)key.GetHashCode();
            uint bin = keyHash % (uint)bins.Length;
            uint binLock = bin % NUM_LOCKS;

            // Acquire the lock to the bin we want
            Monitor.Enter(_binLocks[binLock]);

            // Detect if the table was resized while we were getting the lock
            while (_bins != bins)
            {
                // If so, we need to recalculate the bin # based on the new table size and then try getting the lock again
                Monitor.Exit(_binLocks[binLock]);
                bins = _bins;
                bin = keyHash % (uint)bins.Length;
                binLock = bin % NUM_LOCKS;
                Monitor.Enter(_binLocks[binLock]);
            }

            try
            {
                if (bins[bin] == null)
                {
                    // Create a new value
                    outputValue = valueIfNotFound;
                    bins[bin] = AllocateNode(new KeyValuePair<K, V>(key, outputValue), keyHash);
                    Interlocked.Increment(ref _numItemsInDictionary);
                    return false;
                }
                else
                {
                    // Bin is full; see if a value already exists
                    HashTableLinkedListNode iter = bins[bin];
                    HashTableLinkedListNode endOfBin = iter;
                    while (iter != null)
                    {
                        if (_keyComparer.Equals(key, iter.Kvp.Key))
                        {
                            // Found it!
                            outputValue = iter.Kvp.Value;
                            return true;
                        }

                        iter = iter.Next;
                        if (iter != null)
                        {
                            endOfBin = iter;
                        }
                    }

                    // If value is not already there, append a new entry to the end of the bin
                    outputValue = valueIfNotFound;
                    endOfBin.Next = AllocateNode(new KeyValuePair<K, V>(key, outputValue), keyHash);

                    Interlocked.Increment(ref _numItemsInDictionary);
                    return false;
                }
            }
            finally
            {
                Monitor.Exit(_binLocks[binLock]);
            }
        }

        /// <summary>
        /// Performs an atomic TryGetValue + SetValue operation on the dictionary.
        /// This method behaves the same as TryGetValue, in that it attempts to retrieve a value from the dictionary
        /// and returns true if the value already exists. HOWEVER, If the value is NOT found in the dictionary,
        /// this method will insert a new value atomically, set the out parameter to be the new value, and return FALSE
        /// to indicate that the value was not originally in the dictionary.
        /// If you are concerned about allocations, avoid using anonymous lambdas as a parameter to this method. Use one
        /// of the precompiled overloads instead.
        /// </summary>
        /// <param name="key">The key to attempt to fetch.</param>
        /// <param name="outputValue">The value retrieved from the dictionary.</param>
        /// <param name="newValueProducer">If the given key is not already in the dictionary, this function will be run to create a new value which will be inserted into the dictionary and then returned.
        /// Keep in mind that an anonymous closure like "() => newValue" will incur allocation cost, so avoid it if possible.</param>
        /// <returns>True if the key was already present in the dictionary.</returns>
        public bool TryGetValueOrSet(K key, out V outputValue, Func<V> newValueProducer)
        {
            // Copy a local reference of the table in case another thread tries to resize it while we are accessing
            HashTableLinkedListNode[] bins = _bins;
            uint keyHash = (uint)key.GetHashCode();
            uint bin = keyHash % (uint)bins.Length;
            uint binLock = bin % NUM_LOCKS;

            // Acquire the lock to the bin we want
            Monitor.Enter(_binLocks[binLock]);

            // Detect if the table was resized while we were getting the lock
            while (_bins != bins)
            {
                // If so, we need to recalculate the bin # based on the new table size and then try getting the lock again
                Monitor.Exit(_binLocks[binLock]);
                bins = _bins;
                bin = keyHash % (uint)bins.Length;
                binLock = bin % NUM_LOCKS;
                Monitor.Enter(_binLocks[binLock]);
            }

            try
            {
                if (bins[bin] == null)
                {
                    // Create a new value
                    outputValue = newValueProducer();
                    bins[bin] = AllocateNode(new KeyValuePair<K, V>(key, outputValue), keyHash);
                    Interlocked.Increment(ref _numItemsInDictionary);
                    return false;
                }
                else
                {
                    // Bin is full; see if a value already exists
                    HashTableLinkedListNode iter = bins[bin];
                    HashTableLinkedListNode endOfBin = iter;
                    while (iter != null)
                    {
                        if (_keyComparer.Equals(key, iter.Kvp.Key))
                        {
                            // Found it!
                            outputValue = iter.Kvp.Value;
                            return true;
                        }

                        iter = iter.Next;
                        if (iter != null)
                        {
                            endOfBin = iter;
                        }
                    }

                    // If value is not already there, append a new entry to the end of the bin
                    outputValue = newValueProducer();
                    endOfBin.Next = AllocateNode(new KeyValuePair<K, V>(key, outputValue), keyHash);

                    Interlocked.Increment(ref _numItemsInDictionary);
                    return false;
                }
            }
            finally
            {
                Monitor.Exit(_binLocks[binLock]);
            }
        }

        /// <summary>
        /// Performs an atomic TryGetValue + SetValue operation on the dictionary.
        /// This method behaves the same as TryGetValue, in that it attempts to retrieve a value from the dictionary
        /// and returns true if the value already exists. HOWEVER, If the value is NOT found in the dictionary,
        /// this method will insert a new value atomically, set the out parameter to be the new value, and return FALSE
        /// to indicate that the value was not originally in the dictionary.
        /// </summary>
        /// <param name="key">The key to attempt to fetch.</param>
        /// <param name="outputValue">The value retrieved from the dictionary.</param>
        /// <param name="newValueProducer">If the given key is not already in the dictionary, this function will be run to create a new value which will be inserted into the dictionary and then returned.</param>
        /// <param name="param1">The argument to pass to the value producer.</param>
        /// <returns>True if the key was already present in the dictionary.</returns>
        public bool TryGetValueOrSet<UserParam1>(K key, out V outputValue, Func<UserParam1, V> newValueProducer, UserParam1 param1)
        {
            // Copy a local reference of the table in case another thread tries to resize it while we are accessing
            HashTableLinkedListNode[] bins = _bins;
            uint keyHash = (uint)key.GetHashCode();
            uint bin = keyHash % (uint)bins.Length;
            uint binLock = bin % NUM_LOCKS;

            // Acquire the lock to the bin we want
            Monitor.Enter(_binLocks[binLock]);

            // Detect if the table was resized while we were getting the lock
            while (_bins != bins)
            {
                // If so, we need to recalculate the bin # based on the new table size and then try getting the lock again
                Monitor.Exit(_binLocks[binLock]);
                bins = _bins;
                bin = keyHash % (uint)bins.Length;
                binLock = bin % NUM_LOCKS;
                Monitor.Enter(_binLocks[binLock]);
            }

            try
            {
                if (bins[bin] == null)
                {
                    // Create a new value
                    outputValue = newValueProducer(param1);
                    bins[bin] = AllocateNode(new KeyValuePair<K, V>(key, outputValue), keyHash);
                    Interlocked.Increment(ref _numItemsInDictionary);
                    return false;
                }
                else
                {
                    // Bin is full; see if a value already exists
                    HashTableLinkedListNode iter = bins[bin];
                    HashTableLinkedListNode endOfBin = iter;
                    while (iter != null)
                    {
                        if (_keyComparer.Equals(key, iter.Kvp.Key))
                        {
                            // Found it!
                            outputValue = iter.Kvp.Value;
                            return true;
                        }

                        iter = iter.Next;
                        if (iter != null)
                        {
                            endOfBin = iter;
                        }
                    }

                    // If value is not already there, append a new entry to the end of the bin
                    outputValue = newValueProducer(param1);
                    endOfBin.Next = AllocateNode(new KeyValuePair<K, V>(key, outputValue), keyHash);

                    Interlocked.Increment(ref _numItemsInDictionary);
                    return false;
                }
            }
            finally
            {
                Monitor.Exit(_binLocks[binLock]);
            }
        }

        /// <summary>
        /// Performs an atomic TryGetValue + SetValue operation on the dictionary.
        /// This method behaves the same as TryGetValue, in that it attempts to retrieve a value from the dictionary
        /// and returns true if the value already exists. HOWEVER, If the value is NOT found in the dictionary,
        /// this method will insert a new value atomically, set the out parameter to be the new value, and return FALSE
        /// to indicate that the value was not originally in the dictionary.
        /// </summary>
        /// <param name="key">The key to attempt to fetch.</param>
        /// <param name="outputValue">The value retrieved from the dictionary.</param>
        /// <param name="newValueProducer">If the given key is not already in the dictionary, this function will be run to create a new value which will be inserted into the dictionary and then returned.</param>
        /// <param name="param1">The first argument to pass to the value producer.</param>
        /// <param name="param2">The second argument to pass to the value producer.</param>
        /// <returns>True if the key was already present in the dictionary.</returns>
        public bool TryGetValueOrSet<UserParam1, UserParam2>(K key, out V outputValue, Func<UserParam1, UserParam2, V> newValueProducer, UserParam1 param1, UserParam2 param2)
        {
            // Copy a local reference of the table in case another thread tries to resize it while we are accessing
            HashTableLinkedListNode[] bins = _bins;
            uint keyHash = (uint)key.GetHashCode();
            uint bin = keyHash % (uint)bins.Length;
            uint binLock = bin % NUM_LOCKS;

            // Acquire the lock to the bin we want
            Monitor.Enter(_binLocks[binLock]);

            // Detect if the table was resized while we were getting the lock
            while (_bins != bins)
            {
                // If so, we need to recalculate the bin # based on the new table size and then try getting the lock again
                Monitor.Exit(_binLocks[binLock]);
                bins = _bins;
                bin = keyHash % (uint)bins.Length;
                binLock = bin % NUM_LOCKS;
                Monitor.Enter(_binLocks[binLock]);
            }

            try
            {
                if (bins[bin] == null)
                {
                    // Create a new value
                    outputValue = newValueProducer(param1, param2);
                    bins[bin] = AllocateNode(new KeyValuePair<K, V>(key, outputValue), keyHash);
                    Interlocked.Increment(ref _numItemsInDictionary);
                    return false;
                }
                else
                {
                    // Bin is full; see if a value already exists
                    HashTableLinkedListNode iter = bins[bin];
                    HashTableLinkedListNode endOfBin = iter;
                    while (iter != null)
                    {
                        if (_keyComparer.Equals(key, iter.Kvp.Key))
                        {
                            // Found it!
                            outputValue = iter.Kvp.Value;
                            return true;
                        }

                        iter = iter.Next;
                        if (iter != null)
                        {
                            endOfBin = iter;
                        }
                    }

                    // If value is not already there, append a new entry to the end of the bin
                    outputValue = newValueProducer(param1, param2);
                    endOfBin.Next = AllocateNode(new KeyValuePair<K, V>(key, outputValue), keyHash);

                    Interlocked.Increment(ref _numItemsInDictionary);
                    return false;
                }
            }
            finally
            {
                Monitor.Exit(_binLocks[binLock]);
            }
        }

        private V GetInternal(K key)
        {
            // Copy a local reference of the table in case another thread tries to resize it while we are accessing
            HashTableLinkedListNode[] bins = _bins;
            uint keyHash = (uint)key.GetHashCode();
            uint bin = keyHash % (uint)bins.Length;
            uint binLock = bin % NUM_LOCKS;

            // Acquire the lock to the bin we want
            Monitor.Enter(_binLocks[binLock]);

            // Detect if the table was resized while we were getting the lock
            while (_bins != bins)
            {
                // If so, we need to recalculate the bin # based on the new table size and then try getting the lock again
                Monitor.Exit(_binLocks[binLock]);
                bins = _bins;
                bin = keyHash % (uint)bins.Length;
                binLock = bin % NUM_LOCKS;
                Monitor.Enter(_binLocks[binLock]);
            }

            try
            {
                if (bins[bin] == null)
                {
                    // Bin is empty.
                    throw new KeyNotFoundException("The key \"" + key.ToString() + "\" was not found in the dictionary");
                }
                else
                {
                    // Bin is full; see if a value already exists
                    HashTableLinkedListNode iter = bins[bin];
                    while (iter != null)
                    {
                        if (_keyComparer.Equals(key, iter.Kvp.Key))
                        {
                            // Found it!
                            return iter.Kvp.Value;
                        }

                        iter = iter.Next;
                    }

                    throw new KeyNotFoundException("The key \"" + key.ToString() + "\" was not found in the dictionary");
                }
            }
            finally
            {
                Monitor.Exit(_binLocks[binLock]);
            }
        }

        private static HashTableLinkedListNode AllocateNode(KeyValuePair<K, V> kvp, uint keyHash)
        {
            HashTableLinkedListNode allocatedBin;
            if (!RECLAIMED_NODES.TryDequeue(out allocatedBin))
            {
                allocatedBin = new HashTableLinkedListNode(kvp, keyHash);
            }
            else
            {
                allocatedBin.Kvp = kvp;
                allocatedBin.CachedHashValue = keyHash;
            }

            return allocatedBin;
        }

        private static void DeallocateNode(HashTableLinkedListNode node)
        {
            RECLAIMED_NODES.Enqueue(node);
        }

        private void ExpandTableIfNeeded()
        {
            if (_numItemsInDictionary < _bins.Length ||
                _numItemsInDictionary >= MAX_TABLE_SIZE)
            {
                return;
            }

            // Acquire all bin locks in order
            for (int binLock = 0; binLock < NUM_LOCKS; binLock++)
            {
                Monitor.Enter(_binLocks[binLock]);
            }

            try
            {
                // Create a new table and copy all existing values to it
                uint moduloFactor = (uint)_bins.Length;
                uint newTableLength = moduloFactor * 2;
                HashTableLinkedListNode[] newTable = new HashTableLinkedListNode[newTableLength];

                // Since we expand the table by exactly double each time, we can take advantage of modulo arithmetic
                // and the fact that each bin on the old table corresponds to at most 2 bins on the new table.
                // So all we have to do is iterate through each source bin and "unzip" it into two new bins.
                // This prevents having to reallocate all of the nodes again, but we have to be careful about dangling pointers.
                foreach (HashTableLinkedListNode sourceBin in _bins)
                {
                    HashTableLinkedListNode sourceIter = sourceBin;
                    HashTableLinkedListNode sourceIterNext = null;
                    HashTableLinkedListNode targetLow = null;
                    HashTableLinkedListNode targetHigh = null;

                    while (sourceIter != null)
                    {
                        sourceIterNext = sourceIter.Next;
                        sourceIter.Next = null;

                        uint keyHash = sourceIter.CachedHashValue;
                        uint targetBin = keyHash % newTableLength;
                        if (targetBin < moduloFactor)
                        {
                            // Sort to low bin
                            if (targetLow == null)
                            {
                                // Bin is empty
                                newTable[targetBin] = sourceIter;
                            }
                            else
                            {
                                targetLow.Next = sourceIter;
                            }

                            targetLow = sourceIter;
                        }
                        else
                        {
                            // Sort to high bin
                            if (targetHigh == null)
                            {
                                // Bin is empty
                                newTable[targetBin] = sourceIter;
                            }
                            else
                            {
                                targetHigh.Next = sourceIter;
                            }

                            targetHigh = sourceIter;
                        }

                        sourceIter = sourceIterNext;
                    }
                }

                _bins = newTable;
            }
            finally
            {
                // Release all locks
                for (int binLock = 0; binLock < NUM_LOCKS; binLock++)
                {
                    Monitor.Exit(_binLocks[binLock]);
                }
            }
        }

        private class FastConcurrentDictionaryEnumerator : IEnumerator<KeyValuePair<K, V>>
        {
            private readonly HashTableLinkedListNode[] _localTableReference;
            private readonly FastConcurrentDictionary<K, V> _owner;
            private readonly object[] _binLocks;
            private bool _finished;
            private uint _currentBinIdx;
            private uint _currentBinListIdx;
            private uint _beginOffset;
            private KeyValuePair<K, V> _current;

            public FastConcurrentDictionaryEnumerator(FastConcurrentDictionary<K, V> owner)
            {
                _owner = owner;
                _localTableReference = _owner._bins;
                _binLocks = _owner._binLocks;
                Reset();
                _beginOffset = 0;
            }

            public FastConcurrentDictionaryEnumerator(FastConcurrentDictionary<K, V> owner, Random rand)
            {
                _owner = owner;
                _localTableReference = _owner._bins;
                _binLocks = _owner._binLocks;
                Reset();
                _beginOffset = (uint)rand.Next(0, _localTableReference.Length);
            }

            public KeyValuePair<K, V> Current => _current;

            object IEnumerator.Current => _current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (_finished)
                {
                    return false;
                }

                while (_currentBinIdx < _localTableReference.Length)
                {
                    uint currentBinIdxWithOffset = (_currentBinIdx + _beginOffset) % (uint)_localTableReference.Length;

                    // Get the bin lock
                    uint binLock = currentBinIdxWithOffset % NUM_LOCKS;

                    // Acquire the lock to the bin we want
                    Monitor.Enter(_binLocks[binLock]);
                    try
                    {
                        // Was the table resized during enumeration?
                        if (_owner._bins != _localTableReference)
                        {
                            // If so, just abort enumeration
                            _finished = true;
                            _current = default(KeyValuePair<K, V>);
                            return false;
                        }

                        HashTableLinkedListNode iter = _localTableReference[currentBinIdxWithOffset];
                        if (iter == null)
                        {
                            // Skip over empty bins
                            _currentBinIdx++;
                            _currentBinListIdx = 0;
                        }
                        else
                        {
                            uint iterIdx = 0;

                            // Iterate within a single bin
                            while (iter != null &&
                                iterIdx < _currentBinListIdx)
                            {
                                iter = iter.Next;
                                iterIdx++;
                            }

                            if (iter == null)
                            {
                                // Finished iterating this bin. Move on
                                _currentBinIdx++;
                                _currentBinListIdx = 0;
                            }
                            else
                            {
                                _current = iter.Kvp;
                                _currentBinListIdx = iterIdx + 1;
                                return true;
                            }
                        }
                    }
                    finally
                    {
                        Monitor.Exit(_binLocks[binLock]);
                    }
                }

                // Reached end of table
                _finished = true;
                _current = default(KeyValuePair<K, V>);
                return false;
            }

            public void Reset()
            {
                _currentBinIdx = 0;
                _currentBinListIdx = 0;
                _current = default(KeyValuePair<K, V>);
                _finished = false;
            }
        }

        private class HashTableLinkedListNode
        {
            public HashTableLinkedListNode(KeyValuePair<K, V> keyValuePair, int keyHash)
            {
                Kvp = keyValuePair;
                CachedHashValue = unchecked((uint)keyHash);
            }

            public HashTableLinkedListNode(KeyValuePair<K, V> keyValuePair, uint keyHash)
            {
                Kvp = keyValuePair;
                CachedHashValue = keyHash;
            }

            public uint CachedHashValue;
            public KeyValuePair<K, V> Kvp;
            public HashTableLinkedListNode Next;
        }
    }
}
