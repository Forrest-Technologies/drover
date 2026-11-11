# Drover C# Style Guide

This guide is binding for all C# in this repository. The build enforces most of it through `.editorconfig`, `BannedSymbols.txt`, and `tools/Drover.Analyzers`. When a tool and this guide disagree, the tool's configuration is wrong.

---

## Core Philosophy

- Always prefer the newest, cleanest C# syntax available. Lean into syntactic sugar.
- Explicit is better than implicit, except for type declarations where `var` removes noise.
- Readable method names eliminate the need for comments. If a method needs a comment to explain what it does, rename it or extract it.
- Cognitive complexity is the signal for extraction. When a method gets hard to read at a glance, split it.
- Factories should be a last resort. Extension method builders cover the vast majority of construction needs and keep things fluent and testable without ceremony.

---

## Language Features

Always lean toward bleeding-edge C# syntax. When a newer, cleaner way exists, use it.

- **Nullable reference types**: Enabled everywhere (`<Nullable>enable</Nullable>`). Zero nullable warnings in committed code.
- **File-scoped namespaces**: Always. `namespace Drover.Engine.Runs;`, never the braced block form.
- **Primary constructors**: Preferred for DI-heavy classes. Parameters are used directly, with no field assignment needed.
- **Target-typed `new`**: Use `new()` whenever the type is clear from context.
- **Collection expressions**: `[]` for empty, `[..source]` for spreads, `["a", "b"]` for literals. Never `new List<T>()`.
- **`var`**: Always prefer `var` for local variable declarations. Let the right-hand side speak for itself. The one exception is a collection expression, which needs a declared type to compile.
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
| Namespace | PascalCase, mirrors folder hierarchy, starts with `Drover.` | `Drover.Engine.Runs` |
| Class / Struct / Record | PascalCase | `RunWorker` |
| Interface | `I` + PascalCase | `ISandboxProvider` |
| Enum type + members | PascalCase | `GrantEffect.Allow` |
| Method | PascalCase, readable verb phrase describing what it does | `GetThread`, `BuildToolDefinitions` |
| Property | PascalCase | `BaseUrl` |
| Constant | PascalCase | `MaxSteps` |
| Private field | camelCase, **no underscore prefix** | `leaseDuration` |
| Local variable / parameter | camelCase | `runId`, `toolResult` |
| Type parameter | `T` or descriptive PascalCase | `TKey`, `TResult` |

### No `Async` suffix

Never suffix method names with `Async`. The `Task` return type is sufficient signal. Two exceptions:

- Both a synchronous and an asynchronous version of the same method exist in the same type.
- The method overrides or implements a member declared outside Drover, such as an EF Core interceptor or a framework interface. It keeps the external name because it must.

```csharp
// ✅
public async Task<Thread> GetThread(Guid id)

// ❌ unless a synchronous GetThread(Guid id) also exists
public async Task<Thread> GetThreadAsync(Guid id)
```

### Method names describe what, not how

Names should read like plain English describing the outcome. If you have to read the body to understand what a method does, the name is wrong.

```csharp
// ✅
public async Task<Thread> GetThread(Guid id)
public bool HasExpired(DateTimeOffset leaseExpiresAt)
public IEnumerable<ToolDefinition> BuildToolDefinitions(Bot bot)

// ❌
public async Task<Thread> QueryDatabaseForThreadById(Guid id)
public bool CheckIfLeaseTimestampIsExpired(DateTimeOffset leaseExpiresAt)
```

---

## Formatting

- Indentation: 4 spaces, never tabs.
- Max line length: 120 characters.
- One blank line between members. Two blank lines before top-level type declarations.
- Allman brace style: opening brace on its own line.
- Single newline at end of file. No trailing whitespace.
- All `using` directives are **global usings** in a single `GlobalUsings.cs` at the project root. No local `using` directives in individual files. The one exception is an alias (`using X = Some.Long.Name;`) that resolves a genuine ambiguity.

---

## Explicit Scope, Always

Every `if`, `else`, `for`, `foreach`, `while`, and `using` block has curly braces, even for single-line bodies. No exceptions.

```csharp
// ✅
if (thread is null)
{
    return NotFound();
}

// ❌
if (thread is null) return NotFound();
if (thread is null)
    return NotFound();
```

---

## Code Organization (Regions)

All non-trivial classes use well-named regions. The name of a region should make the intent of its contents immediately clear. A blank line after the opening tag and before the closing tag is required.

A type is non-trivial when it has more than three members or more than one kind of member. Interfaces, enums, and positional records do not need regions.

**Standard region order:**

1. `#region Private Fields`
2. `#region Constructors` *(only when not using primary constructors)*
3. `#region Properties`
4. `#region Public Methods` or `#region Interface Implementations`
5. `#region Private Methods` or a descriptively named group (e.g., `#region Grant Resolution`, `#region Validation`)

```csharp
public class ThreadService(ThreadRepository threads)
{
    #region Public Methods

    public async Task<Thread> GetThread(Guid id)
    {
        // ...
    }

    #endregion

    #region Private Methods

    private void ValidateTitle(string title)
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
// ❌ one big method doing everything
public async Task ExecuteStep(Run run)
{
    if (run.Status is RunStatus.Running)
    {
        var bot = await bots.GetBot(run.BotId);
        if (bot.Status is BotStatus.Active)
        {
            // resolve grants...
            // build the turn...
            // persist events...
        }
    }
}

// ✅ extracted, each piece readable in isolation
public async Task ExecuteStep(Run run)
{
    var bot = await GetActiveBot(run);
    var turn = await BuildTurn(run, bot);
    await RunTurn(run, turn);
}
```

---

## Dependency Injection

**Primary constructor (always preferred):**

```csharp
public class ThreadService(
    ThreadRepository threads,
    ILogger<ThreadService> logger)
{
    #region Public Methods

    public async Task<Thread> GetThread(Guid id)
    {
        logger.LogInformation("Getting thread {ThreadId}", id);
        return await threads.GetThread(id);
    }

    #endregion
}
```

**Traditional constructor (only for complex initialization that primary constructors can't handle):**

```csharp
public class ThreadService
{
    #region Private Fields

    private readonly ThreadRepository threads;
    private readonly ILogger logger;

    #endregion

    #region Constructors

    public ThreadService(ThreadRepository threads, ILogger<ThreadService> logger)
    {
        this.threads = threads;
        this.logger = logger;
    }

    #endregion
}
```

**Never use:** property injection, field injection, or service locator.

### Layers

- **Repositories** store and retrieve. They live in `Drover.Data`, one concrete class per aggregate, and are the only code that touches `DroverDbContext`.
- **Services** hold business logic. They call repositories and never build queries themselves.
- A repository method is named for what it returns (`GetThread`, `ListThreadsForBot`), returns entities or records, and never returns `IQueryable`.

### DI Lifetime guidelines

| Type | Lifetime |
|---|---|
| Repositories | `Scoped` |
| Stateless services | `Scoped` |
| Stateful services (caches, event hubs) | `Singleton` |
| Configuration | `Singleton` |

---

## Object Construction: Prefer Extension Builders Over Factories

Factories add ceremony and indirection for no benefit in most cases. Prefer extension methods that build fluently on existing types.

```csharp
// ❌ factory for no reason
public class ToolDefinitionFactory
{
    public ToolDefinition Create(Tool tool) { ... }
}

// ✅ extension builder, fluent, no DI required
public static class ToolExtensions
{
    public static ToolDefinition ToDefinition(this Tool tool) =>
        new(tool.QualifiedName, tool.Description)
            .WithInputSchema(tool.InputSchema)
            .WithSensitivity(tool.Sensitivity);
}
```

Use a factory only when object creation requires injected dependencies that cannot reasonably be passed as parameters, and only when the factory itself is the cleanest available solution.

---

## Collections

Always use collection expressions. Never use `new List<T>()` or similar verbose forms.

```csharp
// ✅ empty
List<Bot> bots = [];

// ✅ spread / filter
List<Bot> active = [..bots.Where(bot => bot.IsActive)];

// ✅ literal initialization
List<string> roles = ["owner", "admin", "member"];

// ✅ property default
public ICollection<ToolGrant> Grants { get; set; } = [];

// ❌
var bots = new List<Bot>();
var bots = new List<Bot> { };
public ICollection<ToolGrant> Grants { get; set; } = new List<ToolGrant>();
```

---

## Strings

Always interpolate. Never concatenate with `+`. Never use `string.Format`.

```csharp
// ✅
var message = $"Run {runId} failed after {attempt} attempts";
var url = $"{baseUrl}/api/threads/{threadId}";

// ❌
var message = "Run " + runId + " failed after " + attempt + " attempts";
var message = string.Format("Run {0} failed after {1} attempts", runId, attempt);
```

For multi-line or complex strings, use raw string literals (`"""..."""`).

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
var name = user?.DisplayName ?? "Unknown";
subscriber?.Notify();
settings ??= DefaultSettings;
```

**Null-forgiving operator (`!`):** Use only when you are certain, and only when the compiler cannot infer it. Prefer adding an explicit null check.

**Always initialize properties to avoid warnings:**

```csharp
// ✅
public string Name { get; set; } = string.Empty;
public ICollection<ToolGrant> Grants { get; set; } = [];

// ❌
public string Name { get; set; }
```

---

## Async / Await

- Always `await`. Never `.Result` or `.Wait()`.
- Return `Task` directly (no `async`/`await` wrapper) when there is nothing to do with the result. This avoids an unnecessary state machine.
- Only add `async`/`await` when you act on the result, handle exceptions, or need to do work after the awaited call.
- Avoid long chained `.ContinueWith(...)` and `.Then(...)` style continuations entirely. Always prefer `await`.

```csharp
// ✅ passthrough, no state machine needed
public Task<Thread> GetThread(Guid id) =>
    threads.GetThread(id);

// ✅ await needed because we act on the result
public async Task<Thread> GetThread(Guid id)
{
    var thread = await threads.GetThread(id);
    logger.LogInformation("Fetched thread {ThreadId}", id);
    return thread;
}

// ❌
var thread = threads.GetThread(id).Result;
threads.Save(thread).Wait();
threads.GetThread(id).ContinueWith(t => DoSomething(t.Result));
```

---

## Logging

- Inject `ILogger<T>` in every class that does meaningful work.
- Always use structured logging: named message template parameters, never interpolated strings in log calls.
- Never `Console.WriteLine` in non-test code.
- Never swallow exceptions silently.
- Never log a secret, a credential, or message content.

```csharp
// ✅
logger.LogError(ex, "Failed to execute run {RunId}", runId);
logger.LogInformation("Run {RunId} completed in {ElapsedMs}ms", runId, elapsed);

// ❌
logger.LogError(ex, $"Failed to execute run {runId}");
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
| Local `using` directives in files | Global usings in `GlobalUsings.cs` |

---

## Before You Commit

Run these. They check most of the list below for you.

```bash
dotnet build Drover.slnx -warnaserror
dotnet format Drover.slnx --verify-no-changes
dotnet test --solution Drover.slnx
```

- [ ] No nullable warnings
- [ ] `var` used for all local variable declarations
- [ ] All strings use `$"..."` interpolation: no `+` concatenation, no `string.Format`
- [ ] All collections initialized with `[]`
- [ ] All `if`/`else`/`for`/`foreach` blocks have curly braces
- [ ] No `.Result`, `.Wait()`, or `.ContinueWith(...)` calls
- [ ] No silent `catch { }` blocks: log and rethrow or handle explicitly
- [ ] Log calls use structured parameters, not interpolation
- [ ] No `Console.WriteLine`: `ILogger<T>` used throughout
- [ ] File-scoped namespaces (`namespace X;`)
- [ ] All usings are global usings in `GlobalUsings.cs`
- [ ] Primary constructors used where appropriate
- [ ] Regions present, well-named, and in standard order
- [ ] No `Async` suffix unless a sync twin exists or an external member requires it
- [ ] No factories where an extension builder would do
