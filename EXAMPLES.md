# SimpleInjection Examples

This document provides more detailed examples of how to use `SimpleInjection` in different scenarios.

---

## Example 1: ASP.NET Core Web API

This example demonstrates how to use `SimpleInjection` to manage dependencies in a minimal ASP.NET Core Web API. It highlights the use of `[Singleton]` for a database service and `[Scoped]` for a request-specific user service.

### 1. Project Setup

Ensure you have a minimal Web API project. Your `.csproj` should reference `SimpleInjection`.

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SimpleInjection" Version="1.0.0" />
  </ItemGroup>
</Project>
```

### 2. Define Your Services

Create services with lifetime attributes. Note how `RequestService` is registered against the `IRequestService` interface.

```csharp
// Services/DatabaseService.cs
[Singleton]
public class DatabaseService
{
    public Guid Id { get; } = Guid.NewGuid();
    public Task<string> GetDataAsync() => Task.FromResult($"Data from DB {Id}");
}

// Services/IRequestService.cs
public interface IRequestService
{
    Guid Id { get; }
    Task<string> GetRequestDataAsync();
}

// Services/RequestService.cs
[Scoped(typeof(IRequestService))]
public class RequestService(DatabaseService databaseService) : IRequestService
{
    public Guid Id { get; } = Guid.NewGuid();
    public Task<string> GetRequestDataAsync() => databaseService.GetDataAsync();
}
```

### 3. Integrate with `Program.cs`

Initialize the `SimpleInjection.Host` and use its `CreateScope()` method to create a new scope for each HTTP request. This is crucial for managing `[Scoped]` services correctly.

```csharp
// Program.cs
using SimpleInjection.Injection;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Initialize the SimpleInjection host
var host = Host.Initialize();

// Create a middleware to manage scope per request
app.Use(async (context, next) =>
{
    // Create a new scope for each request
    using var scope = host.CreateScope();
    context.Items["SimpleInjectionScope"] = scope;
    await next(context);
});

// Define your endpoint
app.MapGet("/", (HttpContext context) =>
{
    // Retrieve the scope from the context
    var scope = context.Items["SimpleInjectionScope"] as IScope;

    // Resolve the services. Note that we ask for the interface.
    var requestService = scope.Get<IRequestService>();
    var dbService = scope.Get<DatabaseService>(); // Resolve singleton to show it's the same instance

    return new
    {
        RequestServiceId = requestService.Id,
        DatabaseServiceId = dbService.Id
    };
});

app.Run();
```

### 4. Run and Test

Run the application and access the `/` endpoint multiple times. You will notice that `RequestServiceId` changes with each request, while `DatabaseServiceId` remains the same, demonstrating the correct lifetime management.

---

## Example 2: Game Inventory System

This example shows how to use the content source generation feature to manage a catalog of game items.

### 1. Define Content Structures

First, define the `INamed` record for your items and the `IContent<T>` collection.

```csharp
// Models/GameItem.cs
using SimpleInjection.Generator;

public record GameItem(string Name, int Value, double Weight) : INamed;

// Data/GameItems.cs
using SimpleInjection.Generator;

[Singleton]
public partial class GameItems : IContent<GameItem>
{
    public GameItem[] All { get; } =
    [
        new("Sword", 100, 5.0),
        new("Shield", 80, 8.5),
        new("Health Potion", 25, 0.5)
    ];
}
```

### 2. Source Generator at Work

The `SimpleInjection` source generator will automatically create the following in the background:

```csharp
// Generated enum
public enum GameItemsType
{
    Sword,
    Shield,
    HealthPotion
}

// Generated helper methods in GameItems class
public partial class GameItems
{
    public GameItem Get(GameItemsType type) => All[(int)type];
    public GameItem this[GameItemsType type] => All[(int)type];
    public GameItem Sword => All[0];
    // ... and so on
}
```

### 3. Use in Your Game Logic

Now, you can use these generated types and helpers in your game logic for type-safe and clean code.

```csharp
// Game.cs
public class Game
{
    private readonly GameItems _items;

    public Game()
    {
        // Assuming the host is initialized elsewhere
        var host = Host.Initialize();
        _items = host.Get<GameItems>();
    }

    public void Run()
    {
        // Access items using the generated enum
        GameItem sword = _items[GameItemsType.Sword];
        Console.WriteLine($"You found a {sword.Name}! It's worth {sword.Value} gold.");

        // Or use the generated properties
        GameItem potion = _items.HealthPotion;
        Console.WriteLine($"You have a {potion.Name} that weighs {potion.Weight}.");
    }
}
```

This approach eliminates "magic strings" and makes your code more robust and easier to refactor.
