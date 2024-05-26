using JetBrains.Annotations;

namespace Haisl.Utils;

[PublicAPI]
public sealed class DynamicObjectPool<TObject> : IAsyncDisposable where TObject : class
{
    #region Definitions for the people with limited short term memory like me

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    //                                                                                                                                                       //
    //  Items can not be deleted from the pool.  They can only be rented out or returned to the pool.                                                        //
    //                                                                                                                                                       //
    //  The only way items can be effectively deleted and disposed of is by calling ClearAsync, which will dispose of all items in the pool or by calling    //
    //  ResizeAsync, which will resize the pool and dispose of surplus items.                                                                                //
    //                                                                                                                                                       //
    //  Disposing the pool will dispose of all items in the pool.                                                                                            //
    //                                                                                                                                                       //
    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #endregion

    /// <summary>The factory method that creates new objects for the pool on demand.</summary>
    private readonly Func<TObject> _objectFactory;

    /// <summary>The method that resets the state of an object before it is returned to the pool.</summary>
    private readonly Action<TObject> _objectReset;


    /// <summary>
    /// The list of objects that were created by the pool using the object factory '_objectFactory'.
    /// </summary>
    /// <remarks>
    /// The maximum number of items in this list can not exceed the size defined by 'maxPoolSize'.
    /// </remarks>
    private readonly Dictionary<TObject, TimeSpan> _createdObjects = [];

    /// <summary>
    /// The list of objects that are currently available for rent.
    /// </summary>
    /// <remarks>
    /// This list contains references to objects that are owned by the pool, meaning that all objects in this list are also contained in the '_createdObjects' list.
    /// </remarks>
    private readonly PriorityQueue<TObject, TimeSpan> _pooledObjects = new();

    /// <summary>
    /// A different representation of the '_pooledObjects' list that is used to speed up the search for objects in the pool.
    /// </summary>
    private readonly HashSet<TObject> _pooledObjectsSet = [];

    /// <summary>
    /// The list of objects that should not be returned to the pool but should be disposed of when they are returned to the pool.
    /// </summary>
    /// <remarks>
    /// This list is used to mark objects that are currently rented out but should be disposed of when they are returned to the pool.<br/>
    /// Object can only end up on this list during a resize operation that reduces the pool size.<br/>
    /// Objects will be marked as to-be-deleted when they were rented out during a resize operation that reduced the pool size and fell into the group of objects that could not
    /// be deleted immediately because they were rented out at the time of the resize operation.<br/>
    /// </remarks>
    private readonly Dictionary<TObject, TimeSpan> _toBeDeletedObjects = [];


    private readonly SemaphoreSlim _poolLock    = new(1, 1);
    private readonly SemaphoreSlim _disposeLock = new(1, 1);

    private readonly IStopwatch _poolLifetimeStopwatch;


    private volatile bool _isDisposed;

    private int _maxPoolSize;


    public int MaxPoolSize => _maxPoolSize;

    public int CurrentPoolSize => _createdObjects.Count;

    public int RentedCount => _createdObjects.Count - _pooledObjectsSet.Count;

    /// <summary>
    /// Returns <see langword="true"/> if the pool is empty, otherwise <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The pool is empty if there are no items in the pool that are currently available for rent.
    /// </remarks>
    public bool IsPoolEmpty => RentedCount == CurrentPoolSize;

    /// <summary>
    /// Returns <see langword="true"/> if the pool is full, otherwise <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The pool is full if the number of items in the pool is equal to the maximum pool size.<br/>
    /// </remarks>
    public bool IsPoolFull => CurrentPoolSize == _maxPoolSize;


    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicObjectPool{TObject}"/> class.
    /// </summary>
    /// <remarks>
    /// This is the constructor that should be used in most cases.
    /// </remarks>
    /// <param name="objectGenerator"></param>
    /// <param name="objectReset"></param>
    /// <param name="maxPoolSize"></param>
    public DynamicObjectPool(Func<TObject> objectGenerator, Action<TObject> objectReset, int maxPoolSize) : this(objectGenerator, objectReset, maxPoolSize, new StopwatchWrapper())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicObjectPool{TObject}"/> class with an explicitly defined pool lifetime stopwatch.
    /// </summary>
    /// <remarks>
    /// This constructor is intended for unit testing purposes only.
    /// </remarks>
    /// <param name="objectGenerator"></param>
    /// <param name="objectReset"></param>
    /// <param name="maxPoolSize"></param>
    /// <param name="poolLifetimeStopwatch"></param>
    /// <exception cref="ArgumentNullException"></exception>
    internal DynamicObjectPool(Func<TObject> objectGenerator, Action<TObject> objectReset, int maxPoolSize, IStopwatch poolLifetimeStopwatch)
    {
        _maxPoolSize           = maxPoolSize;
        _objectFactory         = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
        _objectReset           = objectReset ?? throw new ArgumentNullException(nameof(objectReset));
        _poolLifetimeStopwatch = poolLifetimeStopwatch;
    }


    public async Task<TObject?> RentAsync()
    {
        // Can not rent an item from the pool if the pool is disposed of...
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        // Wait for exclusive access to the pool
        await _poolLock.WaitAsync();

        try
        {
            var (success, item) = RentInternal();

            return success ? item : null;
        }
        finally
        {
            // Give up exclusive access to the pool
            _poolLock.Release();
        }
    }

    public async Task<(bool Success, TObject? Item)> TryRentAsync()
    {
        // Can not rent an item from the pool if the pool is disposed of...
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        // Wait for exclusive access to the pool
        await _poolLock.WaitAsync();

        try
        {
            return RentInternal();
        }
        finally
        {
            // Give up exclusive access to the pool
            _poolLock.Release();
        }
    }

    public async Task ReturnAsync(TObject item)
    {
        // We need to enable the return of rented out items even if the pool is disposed of, as we need to dispose of objects that are on the to-be-deleted list ('_toBeDeletedObjects')
        // when they are returned to the pool.
        if (_toBeDeletedObjects.Count <= 0)
        {
            // No rented out items are pending to be deleted...
            ObjectDisposedException.ThrowIf(_isDisposed, this);
        }

        ArgumentNullException.ThrowIfNull(item);

        // Wait for exclusive access to the pool
        await _poolLock.WaitAsync();

        try
        {
            // -------------------------------------------------------------------------------------------------------------------------------------------------

            // Is the returned item one that should not be deleted?
            // We can reach this point even if the pool is disposed of, as we need to delete and dispose of the item when they are returned to the pool.
            // Items on this list are not contained in any other control list anymore, as they were removed from these other lists during a resize operation.
            if (_toBeDeletedObjects.Remove(item))
            {
                // We do not add the item back to the pool, we just dispose of it...
                // Not disposing of the item here would lead to a memory leak if the item holds any resources that need to be released.
                await DisposeObjectAsync(item);

                // The item is disposed of and no longer owned by the pool, so we can delete it from the list of pool-owned-objects...
                _createdObjects.Remove(item);

                // We are done, return...
                return;
            }

            // -------------------------------------------------------------------------------------------------------------------------------------------------

            // It's a "regular" return...
            // If the pool is disposed of, we will not reach this point as we will throw an exception before...

            var isNotOwnedObject = !_createdObjects.ContainsKey(item);
            var isAlreadyReturned = _pooledObjectsSet.Contains(item);

            // Check if the item was created by the pool or a pool owned item was already returned...
            if (isNotOwnedObject || isAlreadyReturned)
            {
                // The returned item is not owned by the pool or was already returned to the pool - just return...
                return;
            }

            try
            {
                // Reset the state of the item before returning it to the pool
                // _objectReset might throw an exception.  We let that exception bubble up...
                _objectReset(item);

                // Only return the rented out item if '_objectReset' did not throw an exception...
                _pooledObjects.Enqueue(item, _poolLifetimeStopwatch.Elapsed);
                _pooledObjectsSet.Add(item);
            }
            catch (Exception)
            {
                // If an exception occurred while resetting the item, we need to dispose of the item before we throw the exception to avoid a memory leak...
                await DisposeObjectAsync(item);

                // And as the item could not be reset, we need to remove it from the list of pool-owned items...
                _createdObjects.Remove(item);

                // An exception occurred while resetting the item - we let that exception bubble up...
                throw;

                // Note on UnitTest Code Coverage
                // ==============================
                // The following line (the closing curly brace) is marked as uncovered in the coverage report as it is not possible to reach this line in a unit test.
                // This is because the previous 'throw' statement will make the method exit immediately before this line is reached and executed.
                // Sounds strange, but this is how code coverage detection works...
            }
        }
        finally
        {
            _poolLock.Release();
        }
    }

    public async Task<int> ResizeAsync(int newSize)
    {
        #region Implementation Notes

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //
        //  The method is intended to resize the pool to the specified size. This could either be an increase or a decrease.  Depending on if it is an
        //  increase or a decrease, different points need to be considered.
        //
        //  Increase
        //  --------
        //    We can reuse items that are marked as to-be-deleted by undoing the removal.  This will make sure that we do not unnecessarily create new items
        //    or have to dispose items that are already created.  If we can reuse item we need to make sure that these items are reset to their initial state
        //    before they are returned to the pool.
        //
        //  Decrease
        //  --------
        //    If we need to reduce the maximum size of the pool to a size that is smaller than the size of the rented out items, we need to reduce the
        //    number of rented out items to the new maximum pool size.  This is done by adding the surplus rented out items to the list of items to be removed.
        //    We do not need to take care of the list of objects to be removed as this is done in the ReturnAsync method.
        //
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        #endregion

        if (newSize < 0)
            throw new ArgumentOutOfRangeException(nameof(newSize), "New size must be non-negative.");

        ObjectDisposedException.ThrowIf(_isDisposed, this);

        // Wait for exclusive access to the pool
        await _poolLock.WaitAsync();

        try
        {
            var currentSize = _createdObjects.Count;

            if (newSize > currentSize)
            {
                // It's an increase...
                // ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

                // First try to recycle items that were marked as to-be-deleted during a prior resize operation that reduced the pool size.
                // Move as many items from the _toBeDeletedObjects list back to the _createdObjects list as needed to fill up the list of pool owned items.
                // We do that to reuse items that were rented out but were marked as to-be-deleted during a prior resize operation that reduced the pool size.
                // This will save us a couple of unnecessary object creations later on.
                // Remember: Only a resize can delete items from the pool!
                var additionalItemCount = newSize - currentSize;
                var removedItems = new List<TObject>();

                // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
                // Note: Do not refactor because of performance reasons...
                foreach (var toBeReusedItem in _toBeDeletedObjects)
                {
                    // We have enough items to fill up the pool...
                    if (additionalItemCount <= 0)
                        break;

                    try
                    {
                        // In fact, we are returning an item to the pool here, so we need to reset the item first
                        // _objectReset might throw an exception.  We let that exception bubble up...
                        _objectReset(toBeReusedItem.Key);

                        _createdObjects.Add(toBeReusedItem.Key, toBeReusedItem.Value);
                        _pooledObjectsSet.Add(toBeReusedItem.Key);

                        removedItems.Add(toBeReusedItem.Key);

                        additionalItemCount--;
                    }
                    catch (Exception)
                    {
                        // Failed to reset the item we try to reuse.  We need to dispose of the item and remove it from the list of pool owned items...
                        await DisposeObjectAsync(toBeReusedItem.Key);
                        _createdObjects.Remove(toBeReusedItem.Key);

                        throw;

                        // Note on UnitTest Code Coverage
                        // ==============================
                        // The following line (the closing curly brace) is marked as uncovered in the coverage report as it is not possible to reach this line in a unit test.
                        // This is because the previous 'throw' statement will make the method exit immediately before this line is reached and executed.
                        // Sounds strange, but this is how code coverage detection works...
                    }
                }

                // As we can not remove items from a list while iterating over it, we need to remove the items in a separate step...
                foreach (var removedItem in removedItems)
                    _toBeDeletedObjects.Remove(removedItem);

                // Now that we have used all items that were marked to be removed, we can adjust the maxPoolSize to enable the pool to create new items as needed...
                _maxPoolSize = newSize;

                return _maxPoolSize;
            }


            // It's a decrease...
            // ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

            // "Pooled Objects" are all objects that are currently available for rent...
            var pooledObjectsList = _pooledObjectsSet.ToList();

            // "Rented Out" objects are all objects owned by the pool ('_createdObjects') that can be found in the _createdObjects list but not in the 'pooledObjectsList' list...
            var rentedOutObjectsList = _createdObjects.Keys.Except(pooledObjectsList).ToList();

            // "Available Objects" are all objects owned by the pool ('_createdObjects') that can NOT be found in the 'pooledObjectsList' list...
            var availableObjectsList = _createdObjects.Keys.Intersect(pooledObjectsList).ToList();

            var objectsToDelete = currentSize - newSize;

            // First we need to delete as many objects as possible from the list of pool owned objects that are not rented out
            var objectsToBeRemovedFromPooleObjects = new List<TObject>();

            foreach (var availableObject in availableObjectsList.TakeWhile(_ => objectsToDelete > 0))
            {
                // We do not need to "Reset" the item as it is disposed and deleted from the pool...

                await DisposeObjectAsync(availableObject);
                _createdObjects.Remove(availableObject);
                objectsToBeRemovedFromPooleObjects.Add(availableObject);
                objectsToDelete--;
            }

            // Now adjust the list of pooled items...
            RemoveObjectsFromPooledObjectsList(objectsToBeRemovedFromPooleObjects);

            // If there are still objects to delete, we need to mark rented out objects as to-be-deleted...
            if (objectsToDelete > 0)
            {
                // There are still objects to delete, so we need to delete rented out objects...
                foreach (var rentedOutItem in rentedOutObjectsList.TakeWhile(_ => objectsToDelete > 0))
                {
                    _toBeDeletedObjects.Add(rentedOutItem, _createdObjects[rentedOutItem]);
                    _createdObjects.Remove(rentedOutItem);

                    objectsToDelete--;
                }
            }

            _maxPoolSize = newSize;

            return _maxPoolSize;
        }
        finally
        {
            _poolLock.Release();
        }
    }

    public async Task<int> ShrinkAsync(TimeSpan retentionTime)
    {
        // Shrink will remove all items from the pool that are older than a specified time.

        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var currentElapsed = _poolLifetimeStopwatch.Elapsed;
        var expiredItemsList = _createdObjects.Where(entry => currentElapsed - entry.Value >= retentionTime)
            .Select(i => i.Key)
            .ToList();

        var pooledObjectsList  = _pooledObjectsSet.ToList();
        var rentedOutItemsList = _createdObjects.Keys.Except(pooledObjectsList).ToList();

        // Remove all expired items from the list of items owned by the pool...
        foreach (var expiredItem in expiredItemsList)
        {
            // Is the expired item rented out?
            if (rentedOutItemsList.Contains(expiredItem))
            {
                // Can not dispose expired item as it is rented out.  Instead, we need to add it to the list of items to be removed...
                var lastRented = _createdObjects[expiredItem];
                _toBeDeletedObjects.Add(expiredItem, lastRented);
                _createdObjects.Remove(expiredItem);
                continue;
            }

            // It's a pooled item that is not rented out, so we can dispose of it immediately...
            _createdObjects.Remove(expiredItem);
            await DisposeObjectAsync(expiredItem);
        }

        // Now we need to adjust the list of pooled items.
        RemoveObjectsFromPooledObjectsList(expiredItemsList);

        // Return the new pool size...
        return _createdObjects.Count;
    }

    public async Task <bool> ContainsAsync(TObject item)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        // Wait for exclusive access to the pool
        await _poolLock.WaitAsync();

        try
        {
            return _pooledObjectsSet.Contains(item);
        }
        finally
        {
            _poolLock.Release();
        }
    }

    public async Task ClearAsync()
    {
        // Wait for exclusive access to the pool
        await _poolLock.WaitAsync();

        try
        {
            var pooledObjectsList = _pooledObjects.UnorderedItems.Select(i => i.Element).ToList();
            var rentedOutItems = _createdObjects.Keys.Except(pooledObjectsList).ToList();

            // First we need to add all rented out items on the list of items to be removed, so that they are disposed of when returned to the pool.
            foreach (var rentedOutItem in rentedOutItems)
            {
                var lastRented = _createdObjects[rentedOutItem];
                _toBeDeletedObjects.Add(rentedOutItem, lastRented);
            }

            // Now we can dispose of all items owned by the pool that are not rented out...
            foreach (var item in _createdObjects.Keys.Where(item => !rentedOutItems.Contains(item)))
                await DisposeObjectAsync(item);

            _createdObjects.Clear();
            _pooledObjects.Clear();
            _pooledObjectsSet.Clear();
        }
        finally
        {
            _poolLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        await _disposeLock.WaitAsync();

        // Wait for exclusive access to the pool
        await _poolLock.WaitAsync();

        try
        {
            _isDisposed = true;

            // We can dispose pooled items immediately.
            // Rented out items will be disposed of when they are returned to the pool.
            // For that reason the 'ReturnAsync' method will not throw a disposed exception as long as there pending rented out items.

            var pooledObjectsList = _pooledObjects.UnorderedItems.Select(i => i.Element).ToList();

            foreach (var pooledItem in pooledObjectsList)
            {
                await DisposeObjectAsync(pooledItem);
                _createdObjects.Remove(pooledItem);
            }

            _pooledObjects.Clear();
            _pooledObjectsSet.Clear();
            _maxPoolSize = 0;
        }
        finally
        {
            _disposeLock.Release();

            _poolLock.Dispose();
            _disposeLock.Dispose();
        }
    }


    // This method is supposed to "remove" the passed in objects from '_pooledObjects' by collecting all objects that should NOT be removed from that list
    // then clearing the '_pooledObjects' list and re-adding all objects that should not be removed.
    private void RemoveObjectsFromPooledObjectsList(List<TObject> objectsToRemove)
    {
        if (_pooledObjects.Count <= 0)
            return;

        var objectsToKeep = new List<(TObject, TimeSpan)>();

        while (_pooledObjects.TryDequeue(out var item, out var lastRented))
        {
            if (objectsToRemove.Contains(item))
                continue;

            objectsToKeep.Add((item, lastRented));
        }

        _pooledObjects.Clear();
        _pooledObjectsSet.Clear();


        foreach (var (item, lastRented) in objectsToKeep)
        {
            _pooledObjects.Enqueue(item, lastRented);
            _pooledObjectsSet.Add(item);
        }
    }

    private (bool Success, TObject? Item) RentInternal()
    {
        // Try to get an item from the pool and return it...
        if (_pooledObjects.TryDequeue(out var objectToRent, out _))
        {
            _pooledObjectsSet.Remove(objectToRent);
            _createdObjects[objectToRent] = _poolLifetimeStopwatch.Elapsed;

            return (true, objectToRent);
        }

        // All items of the pool are currently rented out...
        // Check if the pool has reached its maximum size.  If so, return null...
        if (IsPoolFull)
            return (false, null);

        // Create a new item and add it to the pool
        // _objectFactory might throw an exception - we let that exception bubble up...
        var newItem = _objectFactory() ?? throw new InvalidOperationException("The object factory returned a null reference.");

        // Do NOT add the new item to _pooledObjects, as we are renting it out here...
        // But we need to add it to the _createdObjects list as it is an item owned by the pool and on return we want to make sure
        // that only items owned by the pool can be returned.
        _createdObjects.Add(newItem, _poolLifetimeStopwatch.Elapsed);

        return (true, newItem);
    }

    private async Task DisposeObjectAsync(TObject item)
    {
        switch (item)
        {
            case IAsyncDisposable asyncDisposableItem:
                await asyncDisposableItem.DisposeAsync();
                break;
            case IDisposable disposableItem:
                disposableItem.Dispose();
                break;
        }
    }
}
