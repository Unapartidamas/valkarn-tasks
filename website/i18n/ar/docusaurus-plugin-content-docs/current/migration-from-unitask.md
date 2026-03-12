---
sidebar_position: 3
title: الترحيل من UniTask
---

# الترحيل من UniTask

Valkarn Tasks متوافق مع واجهة برمجة UniTask في معظم الحالات الشائعة. يغطي هذا الدليل الفروقات.

## إعادة تسمية الأنواع

| UniTask | Valkarn Tasks |
|---------|--------------|
| `UniTask` | `ValkarnTask` |
| `UniTask<T>` | `ValkarnTask<T>` |
| `UniTaskCompletionSource` | `ValkarnTaskCompletionSource` |
| `UniTaskCompletionSource<T>` | `ValkarnTaskCompletionSource<T>` |

## مساحة الأسماء

```csharp
// قبل
using Cysharp.Threading.Tasks;

// بعد
using UnaPartidaMas.Valkarn.Tasks;
```

## الإلغاء التلقائي

يتطلب UniTask إدارة يدوية لـ `CancellationTokenSource`. Valkarn Tasks يولّدها تلقائيًا:

```csharp
// UniTask (يدوي)
public class EnemyAI : MonoBehaviour
{
    CancellationTokenSource _cts;

    void OnEnable() => _cts = new CancellationTokenSource();
    void OnDestroy() => _cts.Cancel();

    async UniTaskVoid Chase(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Move();
            await UniTask.Yield(ct);
        }
    }
}

// Valkarn Tasks (مُولَّد تلقائيًا)
public partial class EnemyAI : MonoBehaviour
{
    async ValkarnTask Chase()
    {
        while (true)
        {
            Move();
            await ValkarnTask.Yield(); // يُلغى تلقائيًا عند التدمير
        }
    }
}
```

## توقيتات PlayerLoop

يوسّع Valkarn Tasks من 10 إلى 16 توقيتًا. جميع توقيتات UniTask تُعيَّن مباشرةً:

| UniTask | Valkarn Tasks |
|---------|--------------|
| `PlayerLoopTiming.Initialization` | `PlayerLoopTiming.Initialization` |
| `PlayerLoopTiming.LastInitialization` | `PlayerLoopTiming.LastInitialization` |
| `PlayerLoopTiming.EarlyUpdate` | `PlayerLoopTiming.EarlyUpdate` |
| `PlayerLoopTiming.FixedUpdate` | `PlayerLoopTiming.FixedUpdate` |
| `PlayerLoopTiming.Update` | `PlayerLoopTiming.Update` |
| `PlayerLoopTiming.LastUpdate` | `PlayerLoopTiming.LastUpdate` |
| `PlayerLoopTiming.PreLateUpdate` | `PlayerLoopTiming.PreLateUpdate` |
| `PlayerLoopTiming.LastPostLateUpdate` | `PlayerLoopTiming.LastPostLateUpdate` |

جديد في Valkarn Tasks: `PreUpdate`، `LastPreUpdate`، `LastPreLateUpdate`، `PostLateUpdate`، `TimeUpdate`، `LastTimeUpdate`.

## Fire-and-Forget

```csharp
// UniTask
UniTask.Void(async () => { ... });

// Valkarn Tasks
ValkarnTask.Forget(MyMethod());
// أو زيّن الطريقة:
[FireAndForget]
async ValkarnTask MyMethod() { ... }
```

## ما هو أفضل في Valkarn Tasks

- **إلغاء تلقائي** عبر مولّد الكود — لا بويلربلايت
- **17 قاعدة محلل** — اكتشاف الأخطاء عند الترجمة
- **Burst / ECS** — كومة مؤقت أصلي، أنظمة ECS غير متزامنة
- **6 توقيتات PlayerLoop إضافية**
- **مجموعة موارد واعية بالخيوط** — لا عمليات ذرية في الخيط الرئيسي
