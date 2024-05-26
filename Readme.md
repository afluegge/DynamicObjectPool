![.NET Version](https://img.shields.io/badge/.NET-8.0-blue)
![License](https://img.shields.io/badge/License-MIT-green)
[![Build Status](https://github.com/afluegge/DynamicObjectPool/actions/workflows/build.yml/badge.svg)](https://github.com/afluegge/DynamicObjectPool/actions/workflows/build.yml)

[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=afluegge_DynamicObjectPool&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=afluegge_DynamicObjectPool)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=afluegge_DynamicObjectPool&metric=coverage)](https://sonarcloud.io/summary/new_code?id=afluegge_DynamicObjectPool)
[![Code Smells](https://sonarcloud.io/api/project_badges/measure?project=afluegge_DynamicObjectPool&metric=code_smells)](https://sonarcloud.io/summary/new_code?id=afluegge_DynamicObjectPool)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=afluegge_DynamicObjectPool&metric=sqale_rating)](https://sonarcloud.io/summary/new_code?id=afluegge_DynamicObjectPool)
[![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=afluegge_DynamicObjectPool&metric=security_rating)](https://sonarcloud.io/summary/new_code?id=afluegge_DynamicObjectPool)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=afluegge_DynamicObjectPool&metric=reliability_rating)](https://sonarcloud.io/summary/new_code?id=afluegge_DynamicObjectPool)

# DynamicObjectPool

DynamicObjectPool is a C# library that provides a flexible and efficient way to manage object pooling. It allows you to dynamically resize the pool, ensuring optimal resource usage and performance.

## Features
- Dynamic Resizing: Adjust the pool size at runtime.
- Thread-Safe: Safe to use in multi-threaded environments.
- Async Support: Fully supports asynchronous operations.
- Customizable: Easily integrate with your custom object creation and reset logic.

## Installation

To install DynamicObjectPool, you can use NuGet:

```cmd
dotnet add package DynamicObjectPool
```

## Usage

### Creating a Pool

To create a pool, you need to provide a factory method for creating new objects and a reset method for reinitializing objects when they are returned to the pool.

```csharp
var pool = new DynamicObjectPool<MyObject>(
    maxPoolSize: 10,
    objectFactory: () => new MyObject(),
    objectReset: obj => obj.Reset()
);
```

### Renting and Returning Objects

You can rent objects from the pool and return them when done.

```csharp
var myObject = await pool.RentAsync();
if (myObject != null)
{
    // Use the object
    myObject.DoSomething();

    // Return the object to the pool
    await pool.ReturnAsync(myObject);
}
else
{
    // Handle the case where the pool is full
    Console.WriteLine("No objects available in the pool.");
}
```

### Resizing the Pool

You can resize the pool at runtime to increase or decrease its capacity.

```csharp
await pool.ResizeAsync(newMaxSize: 20);
```

Checking Pool Status
You can check if the pool is full or empty.

```csharp
bool isFull = pool.IsPoolFull;
bool isEmpty = pool.IsPoolEmpty;
```

## Points to Consider
- Thread Safety: Ensure that your object factory and reset methods are thread-safe.
- Object Lifecycle: Be mindful of the lifecycle of objects in the pool. Objects should be properly reset before being returned to the pool.
- Exception Handling: Handle exceptions that may occur during object creation or reset to avoid leaving the pool in an inconsistent state.

## Example
Here's a complete example demonstrating how to use DynamicObjectPool:

```csharp
public class MyObject
{
    public void Reset()
    {
        // Reset object state
    }

    public void DoSomething()
    {
        // Perform some action
    }
}

public async Task Main()
{
    var pool = new DynamicObjectPool<MyObject>(
        maxPoolSize: 10,
        objectFactory: () => new MyObject(),
        objectReset: obj => obj.Reset()
    );

    var myObject = await pool.RentAsync();
    if (myObject != null)
    {
        myObject.DoSomething();
        await pool.ReturnAsync(myObject);
    }
    else
    {
        Console.WriteLine("No objects available in the pool.");
    }

    await pool.ResizeAsync(newMaxSize: 20);
}
```

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests on GitHub.

## License

This project is licensed under the MIT License. See the LICENSE file for details.
