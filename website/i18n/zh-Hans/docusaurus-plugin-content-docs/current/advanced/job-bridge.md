---
sidebar_position: 2
title: 作业与 Awaitable 桥接
---

# 作业与 Awaitable 桥接

Valkarn Tasks 提供了一组桥接类型，将 Unity 的作业系统和 `Awaitable` API 连接到 `ValkarnTask` 管道。每个桥接都是一个薄薄的、最小化分配的层；没有任何隐藏的魔法。

此处描述的所有类型都在命名空间 `UnaPartidaMas.Valkarn.Tasks.Bridge` 中，并由 `#if UNITY_5_3_OR_NEWER`（或 `#if UNITY_2023_1_OR_NEWER` 用于 Awaitable 支持）守卫。

---

## JobHandleExtensions——等待单个 JobHandle

最简单的桥接。在任何 `JobHandle` 上调用 `.ToValkarnTask()` 以获得一个在作业完成时完成的 `ValkarnTask`。

```csharp
public static ValkarnTask ToValkarnTask(
    this JobHandle handle,
    PlayerLoopTiming timing          = PlayerLoopTiming.Update,
    CancellationToken cancellationToken = default)
```

### 工作原理

1. **快速路径。** 如果 `handle.IsCompleted` 已经为 true，则立即调用 `handle.Complete()` 并返回 `ValkarnTask.CompletedTask`——零分配，不注册 PlayerLoop。
2. **普通路径。** 租用一个池化的 `JobHandlePromise`，在给定的 `timing` 在 PlayerLoop 上注册，并包装在 `ValkarnTask` 中返回。每帧 `MoveNext()` 调用 `JobHandle.ScheduleBatchedJobs()`（在编辑模式和批量模式下刷新作业队列）然后检查 `handle.IsCompleted`。当 handle 完成时，promise 完成任务并将自身返回对象池。
3. **取消。** 如果 `CancellationToken` 触发，handle 被强制完成（始终调用 `handle.Complete()` 以防止作业系统泄漏）并且任务转换到已取消状态。

### 基本用法

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Jobs;

// 调度作业并立即等待它。
var handle = myJob.Schedule();
await handle.ToValkarnTask();

// 使用非默认时机和取消。
await handle.ToValkarnTask(PlayerLoopTiming.PreUpdate, destroyCancellationToken);
```

---

## JobHandleWhenAll——并行等待多个 JobHandle

当你需要调度几个独立的作业并仅在所有作业完成后才恢复时，使用 `JobHandleExtensions.WhenAll`。

```csharp
// 最简重载：在 Update 时机等待所有 handle。
public static ValkarnTask WhenAll(params JobHandle[] handles)

// 完整重载：可配置时机和取消。
public static ValkarnTask WhenAll(
    JobHandle[]       handles,
    PlayerLoopTiming  timing,
    CancellationToken cancellationToken = default)

// 完整重载的扩展方法别名。
public static ValkarnTask ToValkarnTask(
    this JobHandle[]  handles,
    PlayerLoopTiming  timing              = PlayerLoopTiming.Update,
    CancellationToken cancellationToken   = default)
```

### 工作原理

- **快速路径。** 如果数组中的每个 handle 都已完成，所有 handle 被最终化，立即返回 `ValkarnTask.CompletedTask`。
- **空数组。** 返回 `ValkarnTask.CompletedTask`。
- **普通路径。** 创建一个池化的 `JobHandleArrayPromise`。内部从 `ArrayPool<JobHandle>.Shared` 租用一个 `JobHandle[]`（避免每次调用的堆分配），复制输入的 handle，并在 PlayerLoop 上注册。每帧使用紧凑的交换收缩循环仅迭代仍待处理的 handle，并调用 `JobHandle.ScheduleBatchedJobs()` 以保持工作线程运行。
- **取消。** 所有剩余 handle 被强制完成，任务被取消。

### 用法

```csharp
var handleA = jobA.Schedule();
var handleB = jobB.Schedule();
var handleC = jobC.Schedule();

// 等待所有三个。
await JobHandleExtensions.WhenAll(handleA, handleB, handleC);

// 或使用数组上的扩展方法。
JobHandle[] handles = { handleA, handleB, handleC };
await handles.ToValkarnTask();
```

---

## TempNativeArrayScope——跨 await 点的 NativeArray 生命周期

### 问题

使用 `Allocator.TempJob` 分配的 `NativeArray<T>` 生命周期很短。如果你分配一个数组，调度作业，`await` 作业 handle，然后忘记释放数组，Unity 的安全系统会报告内存泄漏。在长异步方法中使用普通的 `try`/`finally` 可以工作，但容易出错。

`TempNativeArrayScope<T>` 是一个包装 `NativeArray<T>` 的结构体，在作用域结束时使用 `using` 语句释放它——RAII 模式应用于原生内存。

### API

```csharp
public struct TempNativeArrayScope<T> : IDisposable where T : struct
{
    // 访问包装的数组。如果已释放则抛出 ObjectDisposedException。
    public NativeArray<T> Array { get; }

    // 如果作用域未被释放且数组已创建，则为 true。
    public bool IsCreated { get; }

    // 使用 Allocator.TempJob 分配新的 NativeArray<T> 并取得所有权。
    public static TempNativeArrayScope<T> Create(int length);

    // 取得已分配的 NativeArray<T> 的所有权。
    public static TempNativeArrayScope<T> Wrap(NativeArray<T> existing);

    // 释放数组。幂等：多次调用是安全的。
    public void Dispose();
}

// 非泛型助手（类型推断便利）。
public static class TempNativeArrayScope
{
    public static TempNativeArrayScope<T> Create<T>(int length) where T : struct;
    public static TempNativeArrayScope<T> Wrap<T>(NativeArray<T> existing) where T : struct;
}
```

`Dispose` 使用普通 `int` 标志而不是 `Interlocked`，因为作用域设计为通过 `using var` 在主线程上单线程使用。

### 用法

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using Unity.Collections;
using Unity.Jobs;

async ValkarnTask ProcessDataAsync(int count, CancellationToken ct)
{
    // using 语句保证在作用域退出时调用 Dispose()，
    // 无论是正常完成、异常还是取消。
    using var inputScope  = TempNativeArrayScope.Create<float>(count);
    using var outputScope = TempNativeArrayScope.Create<float>(count);

    NativeArray<float> input  = inputScope.Array;
    NativeArray<float> output = outputScope.Array;

    // 填充输入，调度作业。
    var job = new MyProcessingJob { Input = input, Output = output };
    var handle = job.Schedule(count, 64);

    // 不阻塞主线程地等待。
    // NativeArray 保持有效——作业仍在运行。
    await handle.ToValkarnTask(cancellationToken: ct);

    // 作业完成。在这里读取结果。
    float total = 0f;
    for (int i = 0; i < count; i++)
        total += output[i];

    UnityEngine.Debug.Log($"总和: {total}");

    // inputScope.Dispose() 和 outputScope.Dispose() 在这里自动运行。
}
```

你也可以包装已分配的数组：

```csharp
var existing = new NativeArray<int>(1024, Allocator.TempJob);
using var scope = TempNativeArrayScope.Wrap(existing);
// scope 拥有 existing 并将释放它。
```

### 常见陷阱：没有作用域的 NativeArray 生命周期

这种模式是错误的，会导致安全系统错误：

```csharp
// 错误：如果发生异常，数组可能超出作业的生命周期或泄漏。
var array = new NativeArray<float>(1024, Allocator.TempJob);
var handle = job.Schedule(1024, 64);
await handle.ToValkarnTask(); // 挂起点——数组必须保持存活
array.Dispose();          // 如果上面发生异常则永远不会到达
```

使用 `TempNativeArrayScope` 或 `try`/`finally` 来保证在所有代码路径上都执行释放。

---

## AwaitableBridge——将 Unity Awaitable 转换为 ValkarnTask

`AwaitableBridge` 提供扩展方法，用于将 Unity 的 `Awaitable` 和 `Awaitable<T>` 类型（自 Unity 2023.1 起可用）转换为 `ValkarnTask` 兼容的等待器。

**注意：** `Awaitable` 也有自己的 `GetAwaiter()`。由于 C# 重载解析始终优先选择实例方法而不是扩展方法，在 `async ValkarnTask` 方法内写 `await myAwaitable` 已经可以正确工作——Unity 的等待器实现了 `ICriticalNotifyCompletion`，`ValkarnTask` 构建器接受它。只有当你想将 `Awaitable` 传给组合器（`ValkarnTask.WhenAll`、`ValkarnTask.WhenAny`）或将其存储为 `ValkarnTask` 变量时，才需要 `.AsValkarnTask()` 扩展方法。

此文件由 `#if UNITY_2023_1_OR_NEWER` 守卫。

### API

```csharp
// 将 Awaitable 转换为 ValkarnTask 兼容的等待器。
public static AwaitableValkarnTaskAwaiter   AsValkarnTask(this Awaitable awaitable)

// 将 Awaitable<T> 转换为 ValkarnTask 兼容的等待器。
public static AwaitableValkarnTaskAwaiter<T> AsValkarnTask<T>(this Awaitable<T> awaitable)
```

两个等待器都实现了 `ICriticalNotifyCompletion`，跳过了 `ExecutionContext` 捕获。它们将 `IsCompleted`、`GetResult` 和 `OnCompleted` 直接委托给包装的 Unity 等待器。

### 用法

```csharp
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnityEngine;

// 直接等待——在 async ValkarnTask 方法中无需转换。
async ValkarnTask DirectExample()
{
    await Awaitable.NextFrameAsync();       // 无需转换。
    await Awaitable.WaitForSecondsAsync(1f);
}

// 显式转换——用于组合器和存储时需要。
async ValkarnTask CombinatorExample()
{
    ValkarnTask a = Awaitable.NextFrameAsync().AsValkarnTask();
    ValkarnTask b = Awaitable.WaitForSecondsAsync(2f).AsValkarnTask();
    await ValkarnTask.WhenAll(a, b);
}

// 带结果类型的泛型版本。
async ValkarnTask<Texture2D> LoadTextureExample(string path)
{
    Awaitable<Texture2D> loadOp = LoadTextureAsync(path);
    return await loadOp.AsValkarnTask();
}
```

---

## JobBridge——源码生成的包装器

`JobBridge.cs` 定义了 `JobPromise<TJob>`，这是源码生成器使用的泛型池化 promise 类型。它是一个实现细节；你通常不会自己实例化它。

```csharp
// 每帧轮询 JobHandle。由源码生成的 ScheduleAsync 方法使用。
public sealed class JobPromise<TJob> : ValkarnTask.ISource, IPlayerLoopItem, IPoolNode<JobPromise<TJob>>
    where TJob : struct
{
    public static JobPromise<TJob> Create(JobHandle handle, CancellationToken ct, out uint token);
}
```

行为与 `JobHandlePromise`（参见 `JobHandleExtensions`）相同，只是它对作业类型是泛型的，以实现对象池隔离——每种作业类型都有自己的对象池。

---

## 源码生成器：JobBridgeGenerator

`JobBridgeGenerator` 是一个 Roslyn 增量源码生成器（类 `UnaPartidaMas.Valkarn.Tasks.SourceGen.Generators.JobBridgeGenerator`），自动为你的作业类型生成 `ScheduleAsync` 扩展方法。

### 检测内容

生成器扫描编译中所有实现以下之一的**公共**结构体：
- `Unity.Jobs.IJob`
- `Unity.Jobs.IJobParallelFor`
- `Unity.Jobs.IJobFor`

私有和内部作业结构体被跳过。如果结构体嵌套在非公共类型中，也会被跳过。

如果 `UnaPartidaMas.Valkarn.Tasks.ValkarnTask` 在编译中未找到，生成器不执行任何操作，因此在不引用 Valkarn Tasks 的程序集中是安全的。

### 生成内容

输出文件是 `ValkarnTask.JobBridge.Generated.g.cs`。对于每个检测到的作业类型，它生成一个 `public static class __<TypeName>_AsyncExt`，包含：

| 作业接口 | 生成的方法签名 |
|---|---|
| `IJob` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, CancellationToken ct = default)` |
| `IJobParallelFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleAsync(this ref MyJob job, int arrayLength, CancellationToken ct = default)` |
| `IJobFor` | `public static ValkarnTask ScheduleParallelAsync(this ref MyJob job, int arrayLength, int innerLoopBatchCount, CancellationToken ct = default)` |

每个生成的方法使用标准 Unity 扩展方法调度作业，然后将生成的 `JobHandle` 包装在 `JobPromise<TJob>` 中并返回一个 `ValkarnTask`。

对于嵌套类型（例如，外部类中的作业结构体），生成的类名使用下划线：`__Outer_Inner_AsyncExt`。

### 生成方法的用法

```csharp
// IJob 示例
public struct MyCalculationJob : IJob
{
    public NativeArray<float> Data;
    public void Execute() { /* ... */ }
}

// 生成器产生：
//   public static ValkarnTask ScheduleAsync(this ref MyCalculationJob job, CancellationToken ct = default)

async ValkarnTask RunCalculation(CancellationToken ct)
{
    using var scope = TempNativeArrayScope.Create<float>(1024);
    var job = new MyCalculationJob { Data = scope.Array };
    await job.ScheduleAsync(ct);  // 生成的扩展方法
    // 在这里从 scope.Array 读取结果。
}

// IJobParallelFor 示例
public struct MyParallelJob : IJobParallelFor
{
    public NativeArray<float> Input;
    public NativeArray<float> Output;
    public void Execute(int index) { Output[index] = Input[index] * 2f; }
}

async ValkarnTask RunParallel(int length, CancellationToken ct)
{
    using var inputScope  = TempNativeArrayScope.Create<float>(length);
    using var outputScope = TempNativeArrayScope.Create<float>(length);
    var job = new MyParallelJob { Input = inputScope.Array, Output = outputScope.Array };
    await job.ScheduleAsync(length, innerLoopBatchCount: 64, ct);
}
```

---

## 源码生成器：AwaitableBridgeGenerator

`AwaitableBridgeGenerator` 检测编译中是否存在 `UnityEngine.Awaitable` 和 `UnityEngine.Awaitable<T>`，并在存在时生成 `AsValkarnTask()` 扩展方法。

输出文件是 `ValkarnTask.AwaitableBridge.Generated.g.cs`。生成的代码位于类 `AwaitableBridgeExtensions` 下的 `namespace UnaPartidaMas.Valkarn.Tasks.Bridge` 中。

生成的方法：

```csharp
// 当找到 UnityEngine.Awaitable 时生成：
public static async ValkarnTask AsValkarnTask(this Awaitable awaitable)
{
    await awaitable;
}

// 当找到 UnityEngine.Awaitable<T> 时生成：
public static async ValkarnTask<T> AsValkarnTask<T>(this Awaitable<T> awaitable)
{
    return await awaitable;
}
```

这些是 `async ValkarnTask` 方法，因此它们经过 Valkarn Tasks 的池化异步构建器——在预热的对象池上是零分配的。

生成器有守卫：如果 `ValkarnTask` 不在编译中，不会生成任何代码。这防止了在引用 Unity 但不引用 Valkarn Tasks 的程序集中出现 CS0246 错误。

---

## 完整工作示例：ECS 系统中的作业桥接

以下来自 `Samples~/ECS/JobBridgeExample.cs`，展示了从 `ISystem` 调度 Burst 并行作业、不阻塞地等待它并写回结果的完整模式。

```csharp
#if UNITY_5_3_OR_NEWER && VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnaPartidaMas.Valkarn.Tasks.ECS;

public partial struct JobBridgeExample : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<HealthData>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var worldCt = state.World.GetWorldCancellationToken();

        // 在 OnUpdate 内同步提取所有实体数据。
        // 异步方法不能有 ref 参数（CS1988），因此数据
        // 必须在这里复制并按值传递给异步方法。
        var query       = SystemAPI.QueryBuilder().WithAll<HealthData>().Build();
        var entityCount = query.CalculateEntityCount();
        if (entityCount == 0) return;

        var entities    = query.ToEntityArray(Allocator.TempJob);
        var healthArray = query.ToComponentDataArray<HealthData>(Allocator.TempJob);
        var results     = new NativeArray<float>(entityCount, Allocator.TempJob);

        // 异步方法取得 NativeArray 的所有权并释放它们。
        ProcessHealthAsync(state.EntityManager, entities, healthArray, results, worldCt).Forget();
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state) { }

    static async ValkarnTask ProcessHealthAsync(
        EntityManager entityManager,
        NativeArray<Entity> entities,
        NativeArray<HealthData> healthArray,
        NativeArray<float> results,
        CancellationToken ct)
    {
        try
        {
            // 阶段 1：调度 Burst 作业。
            var job = new HealthProcessingJob
            {
                HealthInputs     = healthArray,
                ProcessedOutputs = results,
            };
            var handle = job.Schedule(entities.Length, batchSize: 64);

            // 阶段 2：不阻塞主线程地等待完成。
            await handle.ToValkarnTask(cancellationToken: ct);

            // 阶段 3：应用结果。我们回到主线程了。
            ct.ThrowIfCancellationRequested();

            for (int i = 0; i < entities.Length; i++)
            {
                // 作业运行期间实体可能已被销毁。
                if (!entityManager.SafeEntityExists(entities[i]))
                    continue;

                entityManager.SetComponentData(entities[i], new HealthData
                {
                    CurrentHealth = results[i],
                });
            }
        }
        finally
        {
            // 始终释放 NativeArray——在成功、异常或取消时运行。
            if (entities.IsCreated)    entities.Dispose();
            if (healthArray.IsCreated) healthArray.Dispose();
            if (results.IsCreated)     results.Dispose();
        }
    }

    [BurstCompile]
    struct HealthProcessingJob : IJobParallelFor
    {
        [ReadOnly]  public NativeArray<HealthData> HealthInputs;
        [WriteOnly] public NativeArray<float>      ProcessedOutputs;

        public void Execute(int index)
        {
            var h = HealthInputs[index];
            var newHealth = h.CurrentHealth + h.RegenRate;
            if (newHealth > h.MaxHealth) newHealth = h.MaxHealth;
            ProcessedOutputs[index] = newHealth;
        }
    }

    struct HealthData : IComponentData
    {
        public float CurrentHealth;
        public float MaxHealth;
        public float RegenRate;
    }
}
#endif
```

---

## 桥接类型摘要

| 类型 | 用途 | 分配情况 |
|---|---|---|
| `JobHandleExtensions.ToValkarnTask()` | 等待单个 `JobHandle` | 快速路径零分配；否则池化 promise |
| `JobHandleExtensions.WhenAll()` | 并行等待多个 `JobHandle` | 快速路径零分配；否则池化 promise + `ArrayPool` 租用 |
| `TempNativeArrayScope<T>` | `NativeArray` 的 RAII 生命周期管理 | 无（结构体） |
| `AwaitableBridge.AsValkarnTask()` | 将 `Awaitable`/`Awaitable<T>` 转换为 ValkarnTask | 无（结构体等待器） |
| 生成的 `ScheduleAsync()` | 直接等待类型化作业 | 池化 `JobPromise<TJob>` |
