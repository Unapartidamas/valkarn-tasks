# Valkarn Tasks for Unity

> Zero-allocation, struct-based async/await framework for Unity. Faster than UniTask. Source-generated lifecycle cancellation. Burst & ECS ready.

[![Unity](https://img.shields.io/badge/Unity-2023.1%2B-black)](https://unity.com)
[![License](https://img.shields.io/badge/license-free%20%2F%20commercial-blue)](LICENSE.md)

---

## Features

- **Zero allocation** on the happy path — completed tasks cost nothing
- **Struct-based** `ValkarnTask` / `ValkarnTask<T>` — no heap pressure
- **Thread-aware pool** — no atomics on the main thread, Treiber stack for background threads
- **Auto-cancel on destroy** — source-generated `CancellationToken` tied to `MonoBehaviour` lifetime
- **16 PlayerLoop timings** — `Initialization`, `EarlyUpdate`, `FixedUpdate`, `Update`, `LateUpdate`, and more
- **Channels** — bounded and unbounded, fully async producer/consumer
- **Burst & ECS support** — native timer heap, job bridge, async systems
- **Compile-time diagnostics** — 17 analyzer rules catch bugs before they ship

---

## Quick Start

### Install via Unity Package Manager

Add to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.unapartidamas.valkarn.tasks": "https://github.com/unapartidamas/valkarn-tasks.git"
  }
}
```

### Basic usage

```csharp
using UnaPartidaMas.Valkarn.Tasks;

public class MyBehaviour : MonoBehaviour
{
    async ValkarnTask Start()
    {
        await ValkarnTask.Delay(1000); // 1 second, zero alloc
        Debug.Log("Done!");
    }
}
```

### Auto-cancel on destroy (source-generated)

```csharp
// No boilerplate needed — the source generator handles it automatically
public partial class EnemyAI : MonoBehaviour
{
    async ValkarnTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await ValkarnTask.Yield(); // cancels automatically when destroyed
        }
    }
}
```

### WhenAll / WhenAny

```csharp
var (a, b, c) = await ValkarnTask.WhenAll(LoadTexture(), LoadAudio(), LoadData());
var first     = await ValkarnTask.WhenAny(FromCache(), FromNetwork());
```

### Channels

```csharp
var ch = Channel.CreateBounded<int>(capacity: 16);
await ch.Writer.WriteAsync(42);
var value = await ch.Reader.ReadAsync();
```

---

## Comparison

| Feature | `System.Task` | UniTask | Awaitable | **Valkarn Tasks** |
|---------|--------------|---------|-----------|-------------------|
| Allocation (happy path) | High | ~Zero | Zero | **Zero** |
| Struct-based | No | Yes | Yes | **Yes** |
| Auto-cancel on destroy | No | Manual | No | **Source-generated** |
| Burst / ECS support | No | No | Partial | **Yes** |
| Compile-time diagnostics | No | No | No | **17 rules** |
| PlayerLoop timings | 1 | 10 | 1 | **16** |

---

## Requirements

- Unity 2023.1 or later
- .NET Standard 2.1

Optional:
- Unity Entities 1.0+ (ECS integration)
- Unity Burst 1.8+ (Burst scheduler)
- Unity Collections 2.0+ (NativeTimerHeap)

---

## Documentation

Full documentation at **[tasks.valkarn.com](https://tasks.valkarn.com)**

- [Features](https://tasks.valkarn.com/docs/features)
- [Architecture](https://tasks.valkarn.com/docs/architecture)
- [Quick Start](https://tasks.valkarn.com/docs/quick-start)
- [API Reference](https://tasks.valkarn.com/docs/api/vlk-task)
- [Migration from UniTask](https://tasks.valkarn.com/docs/migration-from-unitask)

---

## License

Free for individuals and studios under $1M/year revenue. Commercial license required above that threshold — see [LICENSE.md](LICENSE.md).

---

## Credits

Made by **Una Partida Mas** — [unapartidamas.com](https://unapartidamas.com)

> Powered by Valkarn Tasks
