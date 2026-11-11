# CODE_STYLE.md
# C# Style Guide

---

## Core Philosophy

- Always prefer the newest, cleanest C# syntax available. Lean into syntactic sugar.
- Explicit is better than implicit — except for type declarations where `var` removes noise.
- Readable method names eliminate the need for comments. If a method needs a comment to explain what it does, rename it or extract it.
- Cognitive complexity is the signal for extraction. When a method gets hard to read at a glance, split it.
- Factories should be a last resort. Extension method builders cover the vast majority of construction needs and keep things fluent and testable without ceremony.

---

## Language Features

Always lean toward bleeding-edge C# syntax. When a newer, cleaner way exists, use it.

- **Nullable reference types**: Enabled everywhere (`<Nullable>enable</Nullable>`). Zero nullable warnings in committed code.
- **File-scoped namespaces**: Always. `namespace My.Project.Name;` — never the braced block form.
- **Primary constructors**: Preferred for DI-heavy classes. Parameters are used directly — no field assignment needed.
- **Target-typed `new`**: Use `new()` whenever the type is clear from context.
- **Collection expressions**: `[]` for empty, `[..source]` for spreads, `["a", "b"]` for literals. Never `new List<T>()`.
- **`var`**: Always prefer `var` for local variable declarations. Let the right-hand side speak for itself.
- **Expression-bodied members**: Use for simple single-expression getters and pass-throughs.
- **`nameof`**: Always over hard-coded string literals for member names.
- **String interpolation**: Always use `$"..."`. Never `string.Format(...)`, never `+` concatenation.
- **Pattern matching**: Prefer `is`, `is not`, `switch` expressions, and positional patterns over chains of equality checks.
- **Records**: Use for immutable data shapes, DTOs, and value objects.
- **`required` members**: Use on model properties that must be set at construction.
- **Null-coalescing / null-conditional**: Use `??`, `?.`, `??=` liberally to keep null handling inline and concise.

---

## Naming

| Symbol | Convention | Example |
|---|---|---|
| Namespace | PascalCase, mirrors folder hierarchy | `My.Project.Services` |
| Class / Struct / Record | PascalCase | `RequestExecutor` |
| Interface | `I` + PascalCase | `IRequestRepository` |
| Enum type + members | PascalCase | `AuthScheme.Bearer` |
| Method | PascalCase, readable verb phrase describing what it does | `GetRequest`, `BuildAuthHeader` |
| Property | PascalCase | `BaseUrl` |
| Constant | PascalCase | `MaxRetries` |
| Private field | camelCase, **no underscore prefix** | `connectionString` |
| Local variable / parameter | camelCase | `requestId`, `responseBody` |
| Type parameter | `T` or descriptive PascalCase | `TKey`, `TResult` |

### No `Async` suffix

Never suffix method names with `Async`. The `Task` return type is sufficient signal. The only exception is when both a synchronous and asynchronous version of the same method exist in the same type.

```csharp
// ✅
public async Task<Request> GetRequest(int id)

// ❌ — unless sync GetRequest(int id) also exists
public async Task<Request> GetRequestAsync(int id)
```

### Method names describe what, not how

Names should read like plain English describing the outcome. If you have to read the body to understand what a method does, the name is wrong.

```csharp
// ✅
public async Task<Request> GetRequest(int id)
public bool HasExpired(DateTimeOffset timestamp)
public IEnumerable<Header> BuildAuthHeaders(string token)

// ❌
public async Task<Request> QueryDatabaseForRequestById(int id)
public bool CheckIfTimestampIsExpired(DateTimeOffset timestamp)
```

---

## Formatting

- Indentation: 4 spaces, never tabs.
- Max line length: 120 characters.
- One blank line between members. Two blank lines before top-level type declarations.
- Allman brace style — opening brace on its own line.
- Single newline at end of file. No trailing whitespace.
- All `using` directives are **global usings** in a single `GlobalUsings.cs` at the project root. No local `using` statements in individual files unless resolving an ambiguity.

---

## Explicit Scope — Always

Every `if`, `else`, `for`, `foreach`, `while`, and `using` block has curly braces, even for single-line bodies. No exceptions.

```csharp
// ✅
if (request is null)
{
    return NotFound();
}

// ❌
if (request is null) return NotFound();
if (request is null)
    return NotFound();
```

---

## Code Organization (Regions)

All non-trivial classes use well-named regions. The name of a region should make the intent of its contents immediately clear. A blank line after the opening tag and before the closing tag is required.

**Standard region order:**

1. `#region Private Fields`
2. `#region Constructors` *(only when not using primary constructors)*
3. `#region Properties`
4. `#region Public Methods` or `#region Interface Implementations`
5. `#region Private Methods` or a descriptively named group (e.g., `#region Header Builders`, `#region Validation`)

```csharp
public class RequestRepository(AppDbContext db)
{
    #region Public Methods

    public async Task<Request> GetRequest(int id)
    {
        // ...
    }

    #endregion

    #region Private Methods

    private void ValidateRequest(Request request)
    {
        // ...
    }

    #endregion
}
```

---

## Cognitive Complexity

When a method becomes hard to read at a glance, extract. The heuristic: if you need to scroll or hold more than one level of nesting in your head to understand a method, it needs to be split. Prefer many small, well-named methods over one large method with comments dividing it into sections.

```csharp
// ❌ — one big method doing everything
public async Task Process(Request request)
{
    if (request.Headers.ContainsKey("Authorization"))
    {
        var token = request.Headers["Authorization"];
        if (token.StartsWith("Bearer "))
        {
            // validate...
            // build context...
            // execute...
        }
    }
}

// ✅ — extracted, each piece readable in isolation
public async Task Process(Request request)
{
    var token = ExtractBearerToken(request);
    var context = await BuildExecutionContext(token);
    await Execute(context);
}
```

---

## Dependency Injection

**Primary constructor (always preferred):**

```csharp
public class RequestService(
    IRequestRepository requestRepository,
    ILogger<RequestService> logger)
{
    #region Public Methods

    public async Task<Request> GetRequest(int id)
    {
        logger.LogInformation("Getting request {RequestId}", id);
        return await requestRepository.GetRequest(id);
    }

    #endregion
}
```

**Traditional constructor (only for complex initialization that primary constructors can't handle):**

```csharp
public class RequestService
{
    #region Private Fields

    private readonly IRequestRepository requestRepository;
    private readonly ILogger logger;

    #endregion

    #region Constructors

    public RequestService(IRequestRepository requestRepository, ILogger<RequestService> logger)
    {
        this.requestRepository = requestRepository;
        this.logger = logger;
    }

    #endregion
}
```

**Never use:** property injection, field injection, or service locator.

### DI Lifetime guidelines

| Type | Lifetime |
|---|---|
| Repositories | `Scoped` |
| Stateless services | `Scoped` |
| Stateful services (caches, hubs) | `Singleton` |
| Configuration | `Singleton` |

---

## Object Construction — Prefer Extension Builders Over Factories

Factories add ceremony and indirection for no benefit in most cases. Prefer extension methods that build fluently on existing types.

```csharp
// ❌ — factory for no reason
public class RequestMessageFactory
{
    public HttpRequestMessage Create(ApiRequest request) { ... }
}

// ✅ — extension builder, fluent, no DI required
public static class ApiRequestExtensions
{
    public static HttpRequestMessage ToHttpRequestMessage(this ApiRequest request) =>
        new(request.Method, request.Url)
            .WithAuthHeader(request.Token)
            .WithJsonBody(request.Body);
}
```

Use a factory only when object creation requires injected dependencies that cannot reasonably be passed as parameters, and only when the factory itself is the cleanest available solution.

---

## Collections

Always use collection expressions. Never use `new List<T>()` or similar verbose forms.

```csharp
// ✅ — empty
List<Request> requests = [];

// ✅ — spread / filter
var active = [..requests.Where(r => r.IsActive)];

// ✅ — literal initialization
List<string> methods = ["GET", "POST", "PUT", "DELETE"];

// ✅ — property default
public ICollection<Header> Headers { get; set; } = [];

// ❌
var requests = new List<Request>();
var requests = new List<Request> { };
public ICollection<Header> Headers { get; set; } = new List<Header>();
```

---

## Strings

Always interpolate. Never concatenate with `+`. Never use `string.Format`.

```csharp
// ✅
var message = $"Request {requestId} failed after {retryCount} retries";
var url = $"{baseUrl}/api/v1/{resource}";

// ❌
var message = "Request " + requestId + " failed after " + retryCount + " retries";
var message = string.Format("Request {0} failed after {1} retries", requestId, retryCount);
```

For multi-line or complex strings, use raw string literals (`"""..."""`) introduced in C# 11.

```csharp
var json = """
    {
        "id": 1,
        "name": "test"
    }
    """;
```

---

## Null Handling

**Prefer pattern matching:**

```csharp
if (result is null) { return NotFound(); }
if (result is not null) { return Ok(result); }
```

**Use null-coalescing and null-conditional operators to keep null handling inline:**

```csharp
var name = user?.Name ?? "Unknown";
user?.Notify();
config ??= DefaultConfig;
```

**Null-forgiving operator (`!`):** Use only when you are certain, and only when the compiler cannot infer it. Prefer adding an explicit null check.

**Always initialize properties to avoid warnings:**

```csharp
// ✅
public string Name { get; set; } = string.Empty;
public ICollection<Tag> Tags { get; set; } = [];

// ❌
public string Name { get; set; }
```

---

## Async / Await

- Always `await`. Never `.Result` or `.Wait()`.
- Return `Task` directly (no `async`/`await` wrapper) when there is nothing to do with the result — avoids an unnecessary state machine.
- Only add `async`/`await` when you act on the result, handle exceptions, or need to do work after the awaited call.
- Avoid long chained `.ContinueWith(...)` and `.Then(...)` style continuations entirely — always prefer `await`.

```csharp
// ✅ — passthrough, no state machine needed
public Task<Request> GetRequest(int id) =>
    repository.GetRequest(id);

// ✅ — await needed because we act on the result
public async Task<Request> GetRequest(int id)
{
    var request = await repository.GetRequest(id);
    logger.LogInformation("Fetched request {RequestId}", id);
    return request;
}

// ❌
var request = repository.GetRequest(id).Result;
repository.Save(request).Wait();
repository.GetRequest(id).ContinueWith(t => DoSomething(t.Result));
```

---

## Logging

- Inject `ILogger<T>` in every class that does meaningful work.
- Always use structured logging — named message template parameters, never interpolated strings in log calls.
- Never `Console.WriteLine` in non-test code.
- Never swallow exceptions silently.

```csharp
// ✅
logger.LogError(ex, "Failed to execute request {RequestId}", requestId);
logger.LogInformation("Request {RequestId} completed in {ElapsedMs}ms", requestId, elapsed);

// ❌
logger.LogError(ex, $"Failed to execute request {requestId}");
Console.WriteLine(ex.ToString());
```

**Log levels:**

| Level | When |
|---|---|
| `Trace` | Verbose entry/exit diagnostics |
| `Debug` | Internal state useful during development |
| `Information` | Normal application flow milestones |
| `Warning` | Unexpected but handled conditions |
| `Error` | Failures that affect the current operation |
| `Critical` | System-level failures |

**Exception handling:**

```csharp
try
{
    await DoWork();
}
catch (Exception ex)
{
    logger.LogError(ex, "Failed to do work");
    throw;
}
```

Never catch and swallow. Always log and rethrow, or log and handle with intent.

---

## Anti-Patterns

| ❌ Never | ✅ Instead |
|---|---|
| `"a" + b + "c"` string concat | `$"a{b}c"` interpolation |
| `string.Format(...)` | `$"..."` interpolation |
| `new List<T>()` | `List<T> items = []` |
| `.Result` / `.Wait()` on tasks | `await` |
| `.ContinueWith(...)` chaining | `await` |
| Empty `catch { }` | Log and rethrow |
| `$"message {value}"` in log calls | `"message {Value}", value` structured template |
| `Console.WriteLine(...)` | `logger.LogError(ex, ...)` |
| `_underscore` prefix on fields | bare `camelCase` |
| `namespace X { }` block form | `namespace X;` file-scoped |
| `GetSomethingAsync` (no sync twin) | `GetSomething` |
| Factories for simple construction | Extension method builders |
| `if (x) doThing();` braceless | `if (x) { doThing(); }` always braced |
| Local `using` statements in files | Global usings in `GlobalUsings.cs` |

---

## Pre-Commit Checklist

- [ ] No nullable warnings
- [ ] `var` used for all local variable declarations
- [ ] All strings use `$"..."` interpolation — no `+` concatenation, no `string.Format`
- [ ] All collections initialized with `[]`
- [ ] All `if`/`else`/`for`/`foreach` blocks have curly braces
- [ ] No `.Result`, `.Wait()`, or `.ContinueWith(...)` calls
- [ ] No silent `catch { }` blocks — log and rethrow or handle explicitly
- [ ] Log calls use structured parameters, not interpolation
- [ ] No `Console.WriteLine` — `ILogger<T>` used throughout
- [ ] File-scoped namespaces (`namespace X;`)
- [ ] All usings are global usings in `GlobalUsings.cs`
- [ ] Primary constructors used where appropriate
- [ ] Regions present, well-named, and in standard order
- [ ] No `Async` suffix unless a sync twin exists
- [ ] No factories where an extension builder would do
