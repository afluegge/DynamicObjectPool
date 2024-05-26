using FluentAssertions;
using Haisl.Utils;
using Moq;

namespace DynamicObjectPool.UnitTests;

public class DynamicObjectPoolTests
{
    private readonly Mock<Func<TestPoolObjectAsync>>   _objectAsyncFactory;
    private readonly Mock<Action<TestPoolObjectAsync>> _objectAsyncReset;
    private readonly Mock<Func<TestPoolObject>>        _objectSyncFactory;
    private readonly Mock<Action<TestPoolObject>>      _objectSyncReset;
    private readonly Mock<IStopwatch>                  _stopwatch;


    public DynamicObjectPoolTests()
    {
        var objectCounter = 0;
        var callCount = 1;

        // Set up a stopwatch that returns a time span that increases by one Minute each time it is called and resets the call count when reset is called...
        _stopwatch = new Mock<IStopwatch>();
        _stopwatch.Setup(s => s.Elapsed).Returns(() => TimeSpan.FromMinutes(callCount++));
        _stopwatch.Setup(s => s.Reset()).Callback(() => callCount = 1);

        _objectAsyncFactory = new Mock<Func<TestPoolObjectAsync>>();
        _objectAsyncReset   = new Mock<Action<TestPoolObjectAsync>>();

        _objectAsyncFactory.Setup(f => f()).Returns(() => new TestPoolObjectAsync($"Item {objectCounter++}"));
        _objectAsyncReset.Setup(a => a(It.IsAny<TestPoolObjectAsync>()));


        _objectSyncFactory = new Mock<Func<TestPoolObject>>();
        _objectSyncReset   = new Mock<Action<TestPoolObject>>();

        _objectSyncFactory.Setup(f => f()).Returns(() => new TestPoolObject($"Item {objectCounter++}"));
        _objectSyncReset.Setup(a => a(It.IsAny<TestPoolObject>()));
    }


    // ---[ RentAsync ]---------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RentAsync_ShouldReturnNewObject_WhenPoolIsNotEmptyButNoItemWasEverReturned()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 0, 0);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var result = await asyncPool.RentAsync();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        result!.Name.Should().Be("Item 0");

        // As we had to create the rented object, the factory method should have been called once...
        _objectAsyncFactory.Verify(f => f(), Times.Once);

        // Available objects ready to be rent out are in a reset state as they were reset during return...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task RentAsync_ShouldReturnExistingObject_WhenPoolIsNotEmptyAndAnItemWasReturnedPreviously()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 1, 1);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var result = await asyncPool.RentAsync();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        result!.Name.Should().Be("Item 0");

        // As we can return an existing object, the factory method should not have been called...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // Available objects ready to be rent out are in a reset state as they were reset during return...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task RentAsync_ShouldReturnNull_WhenPoolIsFull()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 10, 0);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var result = await asyncPool.RentAsync();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        result.Should().BeNull();

        // Because no available objects where available, the factory method should have been called.
        // Because the pool is full, we can not create any more objects and therefore the factory method should not have been called
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // Available objects ready to be rent out are in a reset state as they were reset during return...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task RentAsync_ShouldThrowException_WhenObjectFactoryReturnsNull()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        _objectAsyncFactory.Setup(f => f()).Returns(() => null!);
        var asyncPool = new DynamicObjectPool<TestPoolObjectAsync>(_objectAsyncFactory.Object, _objectAsyncReset.Object, 1);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        Func<Task> act = async () => await asyncPool.RentAsync();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("The object factory returned a null reference.");

        // The factory method should have been called once...
        _objectAsyncFactory.Verify(f => f(), Times.Once);

        // Available objects ready to be rent out are in a reset state as they were reset during return...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task RentAsync_ShouldThrowException_WhenObjectFactoryThrowsException()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        _objectAsyncFactory.Setup(f => f()).Returns(() => null!)
            .Callback(() => throw new Exception("Kaboom!"));
        var asyncPool = new DynamicObjectPool<TestPoolObjectAsync>(_objectAsyncFactory.Object, _objectAsyncReset.Object, 1);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        Func<Task> act = async () => await asyncPool.RentAsync();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("Kaboom!");

        // The factory method should have been called once...
        _objectAsyncFactory.Verify(f => f(), Times.Once);

        // Available objects ready to be rent out are in a reset state as they were reset during return...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task RentAsync_ShouldThrowException_WhenCalledOnDisposedPool()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 10, 0);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await asyncPool.DisposeAsync();
        var act = async () => await asyncPool.RentAsync();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().ThrowAsync<ObjectDisposedException>()
            .WithMessage("Cannot access a disposed object*");

        // The factory method should not have been called...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // Available objects ready to be rent out are in a reset state as they were reset during return...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);
    }


    // ---[ TryRentAsync ]-------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TryRentAsync_ShouldReturnTrue_WhenAnObjectCouldBeRented()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 0, 0);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (success, _) = await asyncPool.TryRentAsync();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        success.Should().BeTrue();

        // In this test we do not care about the factory and the reset method being called or not...

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task TryRentAsync_ShouldReturnFalse_WhenNoObjectCouldBeRented()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 10, 0);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (success, _) = await asyncPool.TryRentAsync();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        asyncPool.IsPoolFull.Should().BeTrue();
        success.Should().BeFalse();

        // In this test we do not care about the factory and the reset method being called or not...

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task TryRentAsync_ShouldReturnNewObject_WhenPoolIsNotFullButAllCreatedObjectsAreRentedOut()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 0, 0);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (_, item) = await asyncPool.TryRentAsync();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        item.Should().NotBeNull();
        item!.Name.Should().Be("Item 0");

        // As we had to create the rented object, the factory method should have been called once...
        _objectAsyncFactory.Verify(f => f(), Times.Once);

        // Available objects ready to be rent out are in a reset state as they were reset during return...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task TryRentAsync_ShouldReturnExistingObject_WhenPoolIsNotEmptyAndAtLeastOneObjectIsAvailableForRent()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 4, 1);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (_, item) = await asyncPool.TryRentAsync();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        item.Should().NotBeNull();
        item!.Name.Should().Be("Item 0");

        // As we can return an existing object, the factory method should not have been called...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // Available objects ready to be rent out are in a reset state as they were reset during return...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task TryRentAsync_ShouldThrowException_WhenObjectFactoryReturnsNull()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        _objectAsyncFactory.Setup(f => f()).Returns(() => null!);
        var asyncPool = new DynamicObjectPool<TestPoolObjectAsync>(_objectAsyncFactory.Object, _objectAsyncReset.Object, 1);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        Func<Task> act = async () => await asyncPool.TryRentAsync();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("The object factory returned a null reference.");

        // The factory method should have been called once...
        _objectAsyncFactory.Verify(f => f(), Times.Once);

        // Available objects ready to be rent out are in a reset state as they were reset during return...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task TryRentAsync_ShouldThrowException_WhenObjectFactoryThrowsException()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        _objectAsyncFactory.Setup(f => f()).Returns(() => null!)
            .Callback(() => throw new Exception("Kaboom!"));
        var asyncPool = new DynamicObjectPool<TestPoolObjectAsync>(_objectAsyncFactory.Object, _objectAsyncReset.Object, 1);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        Func<Task> act = async () => await asyncPool.TryRentAsync();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("Kaboom!");

        // The factory method should have been called once...
        _objectAsyncFactory.Verify(f => f(), Times.Once);

        // Available objects ready to be rent out are in a reset state as they were reset during return...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task TryReturnAsync_ShouldThrowException_WhenCalledOnDisposedPool()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 10, 0);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await asyncPool.DisposeAsync();
        var act = async () => await asyncPool.TryRentAsync();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().ThrowAsync<ObjectDisposedException>()
            .WithMessage("Cannot access a disposed object*");

        // The factory method should not have been called as the pool is disposed...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // Available objects ready to be rent out are in a reset state as they were reset during return...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);
    }


    // ---[ ReturnAsync ]--------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ReturnAsync_ShouldDiscardRentedOutObject_ThatGotMarkedAsDeletedAfterResize_WhenObjectIsReturned()
    {
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //                                                                                                                                                     //
        //  This test is a tricky one as not all aspects can be tested directly.  For example, we can not directly test if an object is removed from the pool  //
        //  as the list that contains the pool owned objects (_createdObjects) is private and can not be accessed from the outside.  Instead, we can try to    //
        //  rent out all objects from the pool to get the objects that are currently in the pool.  This requires that no objects are rented out currently.     //
        //  It also is done with the assumption that the pool will generally work as expected.                                                                 //
        //                                                                                                                                                     //
        //  It is further assumed that other tests will fail when something is wrong with the implementation.  If other tests fail, this test might fail       //
        //  as well, but it is not guaranteed.  Right now this is the best we can do...                                                                        //
        //                                                                                                                                                     //
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------

        // This will contain a list of all "Reset" objects...
        // These are the objects that got returned to the pool after the resize.  The objects not in this list but in the 'initiallyOwnedObjectsList' list are the ones that should have been disposed of...
        var resetObjectsList = new List<TestPoolObjectAsync>();

        // Create a pool with 10 objects, all rented out...
        var (asyncPool, initiallyOwnedObjectsList, rentedOutObjects) = await CreatePoolWithAsyncObjects(10, 10, 0);

        // We need to set up the mock in a way that we can track the objects that are reset...
        _objectAsyncReset.Reset();
        _objectAsyncReset.Setup(a => a(It.IsAny<TestPoolObjectAsync>()))
            .Callback<TestPoolObjectAsync>(obj => resetObjectsList.Add(obj));

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // As all 10 objects are rented out, ResizeAsync will mark 5 of the rented out objects as to-be-deleted which will be disposed of when returned...
        // The other 5 objects will be reset and are then available for rent...
        await asyncPool.ResizeAsync(5);

        // Now we return all the initially rented out objects.
        // This will dispose those objects that got marked as to-be-deleted during the resize.  All other objects should be reset and available for rent...
        // All disposed objects should be visible in the 'initiallyOwnedObjectsList' list marked as disposed...
        // The returned objects will be listed in the 'rentedOutObjects' list...
        foreach (var item in rentedOutObjects)
            await asyncPool.ReturnAsync(item);

        var discardedObjectsList    = initiallyOwnedObjectsList.Where(o => o.IsDisposed).ToList();
        var notDiscardedObjectsList = initiallyOwnedObjectsList.Where(o => !o.IsDisposed).ToList();

        var allCurrentlyPooledObjects = new List<TestPoolObjectAsync>();

        // Now we rent out all available objects as this is the only way to get access to the objects that are currently in the pool...
        // We need to do that to verify indirectly that the reset method has correctly removed the previously returned items from the pool that got marked as to-be-deleted
        // in the previously called ResizeAsync method...
        // The assumption is, that RentAsync will only return available objects owned by the pool.  If one to-be-deleted objects had not been removed from the pool
        // we would get it back here...
        while (!asyncPool.IsPoolEmpty)
        {
            var item = await asyncPool.RentAsync();
            allCurrentlyPooledObjects.Add(item!);
        }

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        asyncPool.CurrentPoolSize.Should().Be(5);

        discardedObjectsList.Should().HaveCount(5);
        notDiscardedObjectsList.Should().HaveCount(5);

        resetObjectsList.Should().HaveCount(5);
        resetObjectsList.Should().BeEquivalentTo(notDiscardedObjectsList);

        allCurrentlyPooledObjects.Should().HaveCount(5);
        allCurrentlyPooledObjects.Should().BeEquivalentTo(resetObjectsList);

        // The factory method is never called when returning an object to the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // Returned objects are always reset before returned to the pool but this time we do not return it to the pool but mark it as to-be-deleted, therefore the reset method should not have been called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Exactly(5));

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task ReturnAsync_ShouldNotThrowException_WhenObjectIsReturnedTwice()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 10, 1);
        var item = await asyncPool.RentAsync();

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await asyncPool.ReturnAsync(item!);
        var act = async () => await asyncPool.ReturnAsync(item!);

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().NotThrowAsync<Exception>();

        // The factory method is never called when returning an object to the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // Returned objects are always reset before returned to the pool...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Once);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task ReturnAsync_ShouldNotThrowException_WhenReturnedObjectIsNotPoolOwned()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 10, 0);
        var item = new TestPoolObjectAsync("FooBaa");

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var act = async () => await asyncPool.ReturnAsync(item);

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().NotThrowAsync<Exception>();

        // The factory method is never called when returning an object to the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // Returned objects are always reset before returned to the pool but the returned object is not owned by the pool and therefore the reset method should not have been called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task ReturnAsync_ShouldThrowException_WhenReturnedObjectIsNull()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 10, 0);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var act = async () => await asyncPool.ReturnAsync(null!);

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithMessage("Value cannot be null. (Parameter 'item')");

        // The factory method is never called when returning an object to the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // Returned objects are always reset before returned to the pool but the returned object is null and therefore the reset method should not have been called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task ReturnAsync_ShouldThrowException_WhenReturnedObjectResetThrows()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, rentedOutAsyncObjects) = await CreatePoolWithAsyncObjects(10, 10, 0);

        _objectAsyncReset.Setup(a => a(It.IsAny<TestPoolObjectAsync>()))
            .Callback(() => throw new Exception("Kaboom!"));

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var act = async () => await asyncPool.ReturnAsync(rentedOutAsyncObjects[0]);

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().ThrowAsync<Exception>();

        // The factory method is never called when returning an object to the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // Returned objects are always reset before returned to the pool...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Once);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task ReturnAsync_ShouldThrowException_WhenCalledOnDisposedPoolWithNoPendingReturns()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 10, 10);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await asyncPool.DisposeAsync();
        var act = async () => await asyncPool.ReturnAsync(new TestPoolObjectAsync("FooBaa"));

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().ThrowAsync<ObjectDisposedException>()
            .WithMessage("Cannot access a disposed object*");

        // The factory method is never called when returning an object to the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // Returned objects are always reset before returned to the pool but this time we called ReturnAsync on a disposed pool and therefore the reset method should not have been called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);
    }


    // ---[ ResizeAsync ]--------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ResizeAsync_ShouldNotLoseCreatedObjects_WhenPoolSizeIsDecreased()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // We start with a pool of a maximum size of 10 slots
        var (asyncPool, initiallyOwnedObjectsList, _) = await CreatePoolWithAsyncObjects(12, 12, 12);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await asyncPool.ResizeAsync(5);
        var discardedObjects = initiallyOwnedObjectsList.Where(o => o.IsDisposed).ToList();
        var keptObjects = initiallyOwnedObjectsList.Where(o => !o.IsDisposed).ToList();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        asyncPool.CurrentPoolSize.Should().Be(5);
        keptObjects.Should().HaveCount(5);
        initiallyOwnedObjectsList.Should().Contain(keptObjects);
        discardedObjects.Should().HaveCount(7);

        // The factory method is never called when resizing the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // Objects created by the pool are always in a reset state when available for rent.
        // If objects are drawn from the to-be-deleted list, they are not reset and need to be reset before they are available for rent again...
        // As this is a decrease we do not recycle existing objects and therefore the reset method should not have been called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task ResizeAsync_ShouldAllowToCreateNewObjects_AfterFullPoolSizeWasIncreased()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // We start with a pool of a maximum size of 5 slots with all items rented out.
        // In this situation it is not possible to rent out any more objects.
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(5, 5, 0);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var item = await asyncPool.RentAsync();
        item.Should().BeNull();

        await asyncPool.ResizeAsync(10);
        item = await asyncPool.RentAsync();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        item.Should().NotBeNull();

        // In this test we rent an object from the pool and verify if a new one has been returned.
        // The fact that the factory was called is the indication that a new object has been created.
        _objectAsyncFactory.Verify(f => f(), Times.Exactly(1));

        // Objects created by the pool are always in a reset state when available for rent.
        // If objects are drawn from the to-be-deleted list, they are not reset and need to be reset before they are available for rent again...
        // As this is a decrease we do not recycle existing objects and therefore the reset method should not have been called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task ResizeAsync_ShouldReuseObjectsMarkedAsToBeDeletedAfterPoolSizeShrink_WhenPoolSizeIsIncreased()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // We start with a pool of a maximum size of 10 slots, all rented out...
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 10, 0);

        // Resize the pool to 5, the last 5 objects should be on the to-be-deleted list as all of them are rented out...
        await asyncPool.ResizeAsync(5);
        asyncPool.MaxPoolSize.Should().Be(5);

        // Reset the mock to count the number of times the factory method is called to 0...
        _objectAsyncReset.Setup(a => a(It.IsAny<TestPoolObjectAsync>()));

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // Now we resize the pool to 6 slots, which should reuse one of the objects on the remove list...
        await asyncPool.ResizeAsync(6);

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        asyncPool.MaxPoolSize.Should().Be(6);

        // The factory method is never called when resizing the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // As the pool was resized to 6, one of the objects that was marked as to-be-deleted should have be reused...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Exactly(1));

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task ResizeAsync_ShouldThrowException_WhenPoolSizeIsIncreasedAnObjectHasBeenRecycledButResettingThisObjectDidThrow()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // We start with a pool of a maximum size of 10 slots, all rented out...
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 10, 0);

        // Resize the pool to 5, the last 5 objects should be on the to-be-deleted list as all of them are rented out...
        await asyncPool.ResizeAsync(5);
        asyncPool.MaxPoolSize.Should().Be(5);

        // Reset the mock to count the number of times the factory method is called to 0...
        _objectAsyncReset.Reset();
        _objectAsyncReset.Setup(a => a(It.IsAny<TestPoolObjectAsync>()))
            .Callback(() => throw new Exception("Kaboom!"));

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // Now we resize the pool to 6 slots, which should reuse one of the objects on the remove list...
        var act = async () => await asyncPool.ResizeAsync(6);

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("Kaboom!");

        // The factory method is never called when resizing the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // The verification must happen as an object that got marked as to-be-deleted had not been reset and therefore the reset method should have been called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Exactly(1));

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task ResizeAsync_ShouldThrowException_WhenResizedToNegativeValue()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 10, 0);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var act = async () => await asyncPool.ResizeAsync(-1);

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("New size must be non-negative. (Parameter 'newSize')");

        // The factory method is never called when resizing the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // Negative size values are checked long before reset will be called, therefore the reset method should not have been called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task ResizeAsync_ShouldThrowException_WhenCalledOnDisposedPool()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 10, 0);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await asyncPool.DisposeAsync();
        var act = async () => await asyncPool.ResizeAsync(10);

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().ThrowAsync<ObjectDisposedException>()
            .WithMessage("Cannot access a disposed object*");

        // The factory method is never called when resizing the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // The test for disposal happens long before reset will be called, therefore the reset method should not have been called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);
    }


    // ---[ ShrinkAsync ]--------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ShrinkAsync_ShouldShrinkCorrectlyAndDisposeOnReturn_WhenShrunkItemsAreRentedOut()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // This will create a pool with 10 objects, all rented out. Each created object will have an age stamp incremented by one minute, starting at 1 minute.
        // The first created object has an age of 1 minute while the 10th object has an age of 10 minutes.
        var (asyncPool, initiallyOwnedObjectsList, rentedOutObjects) = await CreatePoolWithAsyncObjects(10, 10, 0, _stopwatch.Object);

        // Now we need to "freeze" the stopwatch at "Elapsed == 10 Minutes" to be able to do the shrink in a controlled way...
        _stopwatch.Reset();
        _stopwatch.Setup(s => s.Elapsed).Returns(() => TimeSpan.FromMinutes(10));

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var newSize = await asyncPool.ShrinkAsync(TimeSpan.FromMinutes(5));

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var disposedObjectsList = initiallyOwnedObjectsList.Where(o => o.IsDisposed).ToList();

        // As all objects are rented out, none of the initially created objects should be disposed of as none of them are returned so far...
        disposedObjectsList.Count.Should().Be(0);

        // We have shrunk down to 5 objects...
        newSize.Should().Be(5);
        asyncPool.CurrentPoolSize.Should().Be(newSize);

        // Now we return all rented out objects to see if they are disposed of correctly
        foreach (var item in rentedOutObjects)
            await asyncPool.ReturnAsync(item);

        disposedObjectsList = initiallyOwnedObjectsList.Where(o => o.IsDisposed).ToList();

        // 5 of the returned objects should be disposed of...
        disposedObjectsList.Count.Should().Be(5);

        // The factory method is never called when shrinking the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // Initially, before the shrink we were dealing with 10 rented out objects.  During the shrink, we marked 5 objects as to-be-deleted.
        // So when returning all 10 initially created objects, 5 of them should be disposed of and 5 of them should be reset...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Exactly(5));
    }

    [Fact]
    public async Task ShrinkAsync_ShouldShrinkCorrectlyAndDisposeImmediately_WhenShrunkItemsWherePooled()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // This will create a pool with 10 objects, all rented out. Each created object will have an age stamp incremented by one minute, starting at 1 minute.
        // The first created object has an age of 1 minute while the 10th object has an age of 10 minutes.
        var (asyncPool, initiallyOwnedObjectsList, rentedOutObjects) = await CreatePoolWithAsyncObjects(10, 10, 10, _stopwatch.Object);

        // Now we need to "freeze" the stopwatch at "Elapsed == 10 Minutes" to be able to do the shrink in a controlled way...
        _stopwatch.Reset();
        _stopwatch.Setup(s => s.Elapsed).Returns(() => TimeSpan.FromMinutes(10));

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var newSize = await asyncPool.ShrinkAsync(TimeSpan.FromMinutes(5));

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var disposedObjectsList = initiallyOwnedObjectsList.Where(o => o.IsDisposed).ToList();

        // As all objects are pooled, 5 of the initially created objects should be disposed of on shrink...
        disposedObjectsList.Count.Should().Be(5);

        // We have shrunk down to 5 objects...
        newSize.Should().Be(5);
        asyncPool.CurrentPoolSize.Should().Be(newSize);

        // The factory method is never called when shrinking the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // Initially, before the shrink we were dealing with 10 pooled objects.  During the shrink, we have disposed of 5 of them immediately.
        // Therefor the reset method should not have been called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);
    }

    [Fact]
    public async Task ShrinkAsync_ShouldThrowException_WhenCalledOnDisposedPool()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        _stopwatch.Setup(s => s.Elapsed).Returns(TimeSpan.FromMinutes(5));
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 10, 0, _stopwatch.Object);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await asyncPool.DisposeAsync();
        var act = async () => await asyncPool.ShrinkAsync(TimeSpan.FromSeconds(10));

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().ThrowAsync<ObjectDisposedException>()
            .WithMessage("Cannot access a disposed object*");

        // The factory method is never called when clearing the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // The reset method is never called when clearing the pool...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);
    }


    // ---[ ClearAsync ]---------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ClearAsync_ShouldDeleteAllObjectsInPool_WhenCalledOnAPoolWithNoRentedOutObjects()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // We start with a pool of a maximum size of 10 slots, none rented out...
        var (asyncPool, initiallyOwnedObjectsList, _) = await CreatePoolWithAsyncObjects(10, 10, 10);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await asyncPool.ClearAsync();

        var disposedObjectsList = initiallyOwnedObjectsList.Where(o => o.IsDisposed).ToList();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        disposedObjectsList.Should().HaveCount(10);

        // The factory method is never called when clearing the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // The reset method is never called when clearing the pool...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task ClearAsync_ShouldDeleteOnlyPooledObjects_WhenCalledOnAPoolWithPartlyRentedOutObjects_AndDisposesThemOfWhenReturned()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // We start with a pool of a maximum size of 6 slots, one of them rented out...
        var (asyncPool, initiallyOwnedObjectsList, _) = await CreatePoolWithAsyncObjects(6, 6, 5);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await asyncPool.ClearAsync();

        var disposedObjectsList = initiallyOwnedObjectsList.Where(o => o.IsDisposed).ToList();
        var rentedOutObject = initiallyOwnedObjectsList.Find(o => !o.IsDisposed);
        rentedOutObject.Should().NotBeNull();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        disposedObjectsList.Should().HaveCount(5);
        await asyncPool.ReturnAsync(rentedOutObject!);

        disposedObjectsList = initiallyOwnedObjectsList.Where(o => o.IsDisposed).ToList();
        disposedObjectsList.Should().HaveCount(6);

        // The factory method is never called when clearing the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // The reset method is never called when clearing the pool...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task ClearAsync_ShouldThrowException_WhenCalledOnDisposedPool()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(10, 10, 0);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await asyncPool.DisposeAsync();
        var act = async () => await asyncPool.ClearAsync();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().ThrowAsync<ObjectDisposedException>()
            .WithMessage("Cannot access a disposed object*");

        // The factory method is never called when clearing the pool...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // The reset method is never called when clearing the pool...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);
    }


    // ---[ ContainsAsync ]------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ContainsAsync_ShouldReturnTrue_WhenCalledForAnExistingObject()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // We start with a pool of a maximum size of 5 slots, none of them rented out...
        var (asyncPool, initiallyOwnedObjectsList, _) = await CreatePoolWithAsyncObjects(5, 5, 5);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var exists = await asyncPool.ContainsAsync(initiallyOwnedObjectsList[0]);

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        exists.Should().BeTrue();

        // The factory method is never called when ContainsAsync is called...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // The reset method is never called when ContainsAsync is called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task ContainsAsync_ShouldReturnFalse_WhenCalledForNonExistingObject()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // We start with a pool of a maximum size of 5 slots, none of them rented out...
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(5, 5, 5);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var exists = await asyncPool.ContainsAsync(new TestPoolObjectAsync("Holla"));

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        exists.Should().BeFalse();

        // The factory method is never called when ContainsAsync is called...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // The reset method is never called when ContainsAsync is called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task ContainsAsync_ShouldThrowException_WhenCalledOnDisposedPool()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // We start with a pool of a maximum size of 5 slots, none of them rented out...
        var (asyncPool, initiallyOwnedObjectsList, _) = await CreatePoolWithAsyncObjects(5, 5, 5);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await asyncPool.DisposeAsync();
        var act = async () => await asyncPool.ContainsAsync(initiallyOwnedObjectsList[0]);

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await act.Should().ThrowAsync<ObjectDisposedException>()
            .WithMessage("Cannot access a disposed object*");

        // The factory method is never called when ContainsAsync is called...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // The reset method is never called when ContainsAsync is called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);
    }


    // ---[ Pool Global Methods ]------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task IsPoolEmpty_ShouldReturnTrue_IfThereAreNoMoreObjectsForRentAvailable()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // We start with a pool of a maximum size of 5 slots, none of them rented out...
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(5, 5, 0);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var isEmpty = asyncPool.IsPoolEmpty;

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        isEmpty.Should().BeTrue();

        // The factory method should never be called...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // The reset method should never be called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task IsPoolEmpty_ShouldReturnFalse_IfThereAreObjectsForRentAvailable()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // We start with a pool of a maximum size of 5 slots, none of them rented out...
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(5, 5, 5);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var isEmpty = asyncPool.IsPoolEmpty;

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        isEmpty.Should().BeFalse();

        // The factory method should never be called...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // The reset method should never be called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task IsPoolFull_ShouldReturnTrue_IfThePoolCanNotCreateMoreObjectsForRent()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // We start with a pool of a maximum size of 5 slots, all slots contain objects, none of them rented out...
        // This is a "Full" pool...
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(5, 5, 5);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var isFull = asyncPool.IsPoolFull;

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        isFull.Should().BeTrue();

        // The factory method should never be called...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // The reset method should never be called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task IsPoolFull_ShouldReturnFalse_IfThePoolStillCanCreateNewObjectsForRent()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // We start with a pool of a maximum size of 5 slots, none of them rented out...
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(5, 4, 4);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var isFull = asyncPool.IsPoolFull;

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        isFull.Should().BeFalse();

        // The factory method should never be called...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // The reset method should never be called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);

        await asyncPool.DisposeAsync();
    }

    [Fact]
    public async Task Pool_ShouldDisposeOfAllCreatedItemsAndMustBeEmpty_WhenDisposed()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        // We start with a pool of a maximum size of 10 slots
        var (asyncPool, initiallyOwnedObjectsList, _) = await CreatePoolWithAsyncObjects(5, 5, 5);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await asyncPool.DisposeAsync();
        var discardedObjects = initiallyOwnedObjectsList.Where(o => o.IsDisposed).ToList();

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        asyncPool.CurrentPoolSize.Should().Be(0);
        discardedObjects.Should().HaveCount(5);

        // The factory method should never be called...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // The reset method should never be called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);
    }

    [Fact]
    public async Task DisposeAsync_ShouldDoNothing_WhenDisposedPoolIsDisposedAgain()
    {
        // Arrange
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        var (asyncPool, _, _) = await CreatePoolWithAsyncObjects(5, 5, 5);
        var (syncPool, _, _)  = await CreatePoolWithSyncObjects(5, 5, 5);

        // Act
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        await asyncPool.DisposeAsync();
        await syncPool.DisposeAsync();
#pragma warning disable S3966
        await asyncPool.DisposeAsync();
        await syncPool.DisposeAsync();
#pragma warning restore S3966

        // Assert
        // -----------------------------------------------------------------------------------------------------------------------------------------------------
        asyncPool.CurrentPoolSize.Should().Be(0);
        syncPool.CurrentPoolSize.Should().Be(0);

        // The factory method should never be called...
        _objectAsyncFactory.Verify(f => f(), Times.Never);

        // The reset method should never be called...
        _objectAsyncReset.Verify(a => a(It.IsAny<TestPoolObjectAsync>()), Times.Never);
    }


    // ========================================================================================================================================================


    #region Test Helper

    /// <summary>
    /// Asynchronously creates a dynamic object pool and rents a specified number of objects from it.
    /// </summary>
    /// <param name="maxPoolSize">The maximum size of the pool.</param>
    /// <param name="createdObjectsCount">The number of pre-created objects owned by the pool.</param>
    /// <param name="poolSize">The number of rented out objects.</param>
    /// <param name="stopwatch"></param>
    /// <returns>A tuple containing the dynamic object pool, a list of pool-owned objects, and a list of rented out objects.</returns>
    /// <remarks>
    /// Use this method to create a new instance of <see langword="DynamicObjectPool{TObject}"/>.  The pool will have the defined maximum pool size and will be
    /// populated with the specified number of objects.  It is possible to define the number of rented out objects.
    /// </remarks>
    public async Task<(DynamicObjectPool<TestPoolObjectAsync> asyncPool, List<TestPoolObjectAsync> poolOwnedAsyncObjects, List<TestPoolObjectAsync> rentedOutAsyncObjects)> CreatePoolWithAsyncObjects(int maxPoolSize, int createdObjectsCount, int poolSize, IStopwatch? stopwatch = null)
    {
        var objectCounter = 0;

        var pool = new DynamicObjectPool<TestPoolObjectAsync>(_objectAsyncFactory.Object, _objectAsyncReset.Object, maxPoolSize, stopwatch ?? new StopwatchWrapper());

        var poolOwnedObjects = new List<TestPoolObjectAsync>();

        // Rent as many objects form the pool to reach the requested number of poo owned objects...
        for (var i = 0; i < createdObjectsCount; i++)
        {
            var item = await pool.RentAsync();
            poolOwnedObjects.Add(item!);
        }

        // Now we return as many objects as required to reach the current pool size...
        var rentedOutObjects = new List<TestPoolObjectAsync>(poolOwnedObjects);

        for (var i = 0; i < poolSize; i++)
        {
            await pool.ReturnAsync(poolOwnedObjects[i]);
            rentedOutObjects.Remove(poolOwnedObjects[i]);
        }

        // We need to redefine these two mock-setups to reset their invocation counters as they were called during the creation of the pool...
        _objectAsyncFactory.Reset();
        _objectAsyncReset.Reset();
        _objectAsyncFactory.Setup(f => f()).Returns(() => new TestPoolObjectAsync($"Item {objectCounter++}"));
        _objectAsyncReset.Setup(a => a(It.IsAny<TestPoolObjectAsync>()));

        return (pool, poolOwnedObjects, rentedOutObjects);
    }


    public async Task<(DynamicObjectPool<TestPoolObject> pool, List<TestPoolObject> poolOwnedObjects, List<TestPoolObject> rentedOutObjects)> CreatePoolWithSyncObjects(int maxPoolSize, int createdObjectsCount, int poolSize, IStopwatch? stopwatch = null)
    {
        var objectCounter = 0;

        var pool = new DynamicObjectPool<TestPoolObject>(_objectSyncFactory.Object, _objectSyncReset.Object, maxPoolSize, stopwatch ?? new StopwatchWrapper());

        var poolOwnedObjects = new List<TestPoolObject>();

        // Rent as many objects form the pool to reach the requested number of poo owned objects...
        for (var i = 0; i < createdObjectsCount; i++)
        {
            var item = await pool.RentAsync();
            poolOwnedObjects.Add(item!);
        }

        // Now we return as many objects as required to reach the current pool size...
        var rentedOutObjects = new List<TestPoolObject>();

        for (var i = 0; i < poolSize; i++)
        {
            await pool.ReturnAsync(poolOwnedObjects[i]);
            rentedOutObjects.Add(poolOwnedObjects[i]);
        }

        // We need to redefine these two mock-setups to reset their invocation counters as they were called during the creation of the pool...
        _objectSyncFactory.Reset();
        _objectSyncReset.Reset();
        _objectSyncFactory.Setup(f => f()).Returns(() => new TestPoolObject($"Item {objectCounter++}"));
        _objectSyncReset.Setup(a => a(It.IsAny<TestPoolObject>()));

        return (pool, poolOwnedObjects, rentedOutObjects);
    }




    public sealed class TestPoolObject(string name) : IDisposable
    {
        public bool   IsDisposed { get; private set; }
        public string Name       { get; } = name;

        public void Dispose()
        {
            IsDisposed = true;
        }
    }



    public sealed class TestPoolObjectAsync(string name) : IAsyncDisposable
    {
        public bool   IsDisposed { get; private set; }
        public string Name       { get; } = name;

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;

            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
