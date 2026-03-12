---
sidebar_position: 1
title: Integração com o Unity PlayerLoop
---

# Integração com o Unity PlayerLoop

O ValkarnTasks não usa threads ou schedulers de background para retomar seus métodos `async`. Tudo é executado na thread principal do Unity, impulsionado pelo próprio sistema PlayerLoop do Unity. Entender como isso funciona irá ajudá-lo a escolher o timing correto para seu caso de uso e a raciocinar exatamente quando seu código é retomado após um `await`.

## O que é o Unity PlayerLoop

O PlayerLoop do Unity é o loop interno do motor que impulsiona cada frame. Não é uma única chamada `Update()` — é uma sequência hierárquica de fases que são executadas em uma ordem definida a cada frame:

```
Initialization
EarlyUpdate
FixedUpdate      (repetido se a física estiver avançando)
PreUpdate
Update
PreLateUpdate
PostLateUpdate
TimeUpdate
```

Cada uma dessas fases de nível superior contém subsistemas que o Unity (e pacotes de terceiros) inserem para executar sua lógica em pontos específicos. `MonoBehaviour.Update()`, por exemplo, é executado dentro da fase `Update`. `MonoBehaviour.LateUpdate()` é executado dentro de `PreLateUpdate`.

Como o ValkarnTasks se conecta diretamente a esse loop, `await ValkarnTask.Yield()` não bloqueia uma thread — ele registra um callback que o Unity chama no próximo tick da fase escolhida e retorna imediatamente.

## Os 16 Timings do PlayerLoop

O ValkarnTasks injeta em 16 pontos: um no **início** e um no **fim** de cada uma das 8 fases do PlayerLoop do Unity. As variantes `Last` são anexadas no final da lista de subsistemas de sua fase pai; as variantes simples são inseridas no início.

| Valor | Inteiro do enum | Fase pai | Posição na fase pai |
|---|---|---|---|
| `Initialization` | 0 | `UnityEngine.PlayerLoop.Initialization` | Primeiro |
| `LastInitialization` | 1 | `UnityEngine.PlayerLoop.Initialization` | Último |
| `EarlyUpdate` | 2 | `UnityEngine.PlayerLoop.EarlyUpdate` | Primeiro |
| `LastEarlyUpdate` | 3 | `UnityEngine.PlayerLoop.EarlyUpdate` | Último |
| `FixedUpdate` | 4 | `UnityEngine.PlayerLoop.FixedUpdate` | Primeiro |
| `LastFixedUpdate` | 5 | `UnityEngine.PlayerLoop.FixedUpdate` | Último |
| `PreUpdate` | 6 | `UnityEngine.PlayerLoop.PreUpdate` | Primeiro |
| `LastPreUpdate` | 7 | `UnityEngine.PlayerLoop.PreUpdate` | Último |
| `Update` | 8 | `UnityEngine.PlayerLoop.Update` | Primeiro |
| `LastUpdate` | 9 | `UnityEngine.PlayerLoop.Update` | Último |
| `PreLateUpdate` | 10 | `UnityEngine.PlayerLoop.PreLateUpdate` | Primeiro |
| `LastPreLateUpdate` | 11 | `UnityEngine.PlayerLoop.PreLateUpdate` | Último |
| `PostLateUpdate` | 12 | `UnityEngine.PlayerLoop.PostLateUpdate` | Primeiro |
| `LastPostLateUpdate` | 13 | `UnityEngine.PlayerLoop.PostLateUpdate` | Último |
| `TimeUpdate` | 14 | `UnityEngine.PlayerLoop.TimeUpdate` | Primeiro |
| `LastTimeUpdate` | 15 | `UnityEngine.PlayerLoop.TimeUpdate` | Último |

`Update` (valor 8) é o **padrão** para todas as operações do ValkarnTasks — `Yield()`, `Delay()`, `WaitUntil()`, `WaitWhile()`, `NextFrame()` e `DelayFrame()`.

## Structs marcadoras e injeção idempotente

Para injetar no PlayerLoop do Unity, o ValkarnTasks cria um `PlayerLoopSystem` por timing. Cada sistema é identificado por uma struct marcadora única definida dentro de `PlayerLoopHelper`:

```csharp
struct ValkarnTaskInitialization     { }
struct ValkarnTaskLastInitialization { }
struct ValkarnTaskEarlyUpdate        { }
struct ValkarnTaskLastEarlyUpdate    { }
struct ValkarnTaskFixedUpdate        { }
struct ValkarnTaskLastFixedUpdate    { }
struct ValkarnTaskPreUpdate          { }
struct ValkarnTaskLastPreUpdate      { }
struct ValkarnTaskUpdate             { }
struct ValkarnTaskLastUpdate         { }
struct ValkarnTaskPreLateUpdate      { }
struct ValkarnTaskLastPreLateUpdate  { }
struct ValkarnTaskPostLateUpdate     { }
struct ValkarnTaskLastPostLateUpdate { }
struct ValkarnTaskTimeUpdate         { }
struct ValkarnTaskLastTimeUpdate     { }
```

Antes de injetar, `PlayerLoopHelper` verifica se `ValkarnTaskUpdate` já aparece em algum lugar na árvore atual do PlayerLoop. Se sim, a injeção é ignorada. Isso torna a injeção **idempotente** — chamar `Init()` múltiplas vezes (o que pode acontecer no editor) nunca resulta em sistemas duplicados sendo registrados.

A injeção sempre lê o PlayerLoop **atual** com `PlayerLoop.GetCurrentPlayerLoop()`, nunca `GetDefaultPlayerLoop()`. Isso significa que qualquer sistema instalado anteriormente por outros pacotes é preservado.

## ContinuationQueue — Callbacks de uso único

Quando você `await ValkarnTask.Yield()` (ou qualquer coisa que suspende por exatamente um tick), a máquina de estados gerada pelo compilador chama `OnCompleted` no awaiter. O awaiter chama `PlayerLoopHelper.AddContinuation(timing, action, state)`, que enfileira o callback no `ContinuationQueue` para aquele timing.

`ContinuationQueue` usa um design de **double-buffer**:

1. **Buffer ativo** (`actionList`): mantém continuações enfileiradas antes e durante o drain atual.
2. **Buffer de espera** (`waitingList`): captura continuações enfileiradas _durante_ o drain do buffer ativo (isto é, enfileiramentos reentrantes de dentro de uma continuação).
3. **Pilha Treiber entre threads** (`crossThreadHead`): continuações postadas de threads de background pousam aqui via compare-and-swap sem bloqueio. No início de cada drain, toda a pilha é atomicamente reivindicada e movida para o buffer ativo.

A cada tick do PlayerLoop, `ContinuationQueue.Run()` executa esta sequência:

```
1. Drenar crossThreadHead → actionList     (take atômico sem bloqueio, depois copiar com SpinLock)
2. Capturar contagem, definir isDraining = true  (com SpinLock)
3. Executar todos os callbacks                   (fora do lock, refs limpas para GC)
4. Trocar actionList ↔ waitingList               (com SpinLock, definir isDraining = false)
```

Após o aquecimento de aproximadamente um ou dois frames, os arrays internos se estabilizam em sua marca máxima e a fila é executada com **zero alocações**.

Nós entre threads são colocados em pool em um pool sem bloqueio limitado (máximo de 1.024 nós) para evitar alocação em cada enfileiramento de thread de background.

## PlayerLoopRunner — Itens recorrentes

Algumas operações devem ser verificadas em cada tick até que sejam concluídas: `Delay()`, `DelayFrame()`, `WaitUntil()`, `WaitWhile()` e `NextFrame()`. Estas implementam a interface `IPlayerLoopItem`:

```csharp
internal interface IPlayerLoopItem
{
    // Retornar true para continuar executando; retornar false para remover do runner.
    bool MoveNext();
}
```

Os itens são adicionados a `PlayerLoopRunner` via `PlayerLoopHelper.AddAction(timing, item)`. A cada tick, `PlayerLoopRunner.Run()` itera todos os itens registrados e chama `MoveNext()` em cada um. Itens que retornam `false` são removidos via **compactação in-place** — o array é compactado em uma única passagem, preservando a ordem de inserção. Itens adicionados durante uma chamada `Run()` são adicionados com segurança e serão coletados no próximo tick.

```
Tick N:
  ContinuationQueue.Run()   → retomar todos os awaiters de uso único
  PlayerLoopRunner.Run()    → dar tick em todos os itens recorrentes (Delay, WaitUntil, ...)
```

Ambos são executados para cada um dos 16 timings, na ordem em que o motor os chama.

O timing `Update` também rastreia a contagem global de frames e periodicamente aciona o trimming do pool de objetos (a cada `ValkarnTask.TrimCheckInterval` frames).

## Inicialização — RuntimeInitializeOnLoadMethod

O ValkarnTasks inicializa em `RuntimeInitializeLoadType.SubsystemRegistration`, o hook mais antigo que o Unity fornece, que dispara antes de `Awake()` em qualquer objeto de cena:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void Init()
{
    // 1. Capturar o ID da thread principal para verificações de segurança de thread
    // 2. Criar ContinuationQueue e PlayerLoopRunner para todos os 16 timings
    // 3. Injetar no PlayerLoop (idempotente)
    // 4. Registrar callback de mudança de estado do modo de jogo (apenas no editor)
}
```

O ID da thread principal é capturado neste ponto e compartilhado com todos os subsistemas de pool e fila para que possam distinguir enfileiramentos da thread principal (caminho SpinLock) de enfileiramentos entre threads (caminho da pilha Treiber).

## Tratamento de Domain Reload (Editor)

No Unity Editor, entrar ou sair do modo de jogo aciona um domain reload. O estado estático da sessão de jogo anterior persistiria e causaria referências obsoletas.

O ValkarnTasks lida com isso com um callback `EditorApplication.playModeStateChanged` registrado durante `Init()`. Quando o estado transita para `ExitingPlayMode` ou `EnteredEditMode`, a seguinte limpeza é executada:

```csharp
// Reiniciar todas as filas e runners
for (int i = 0; i < 16; i++)
{
    s_continuationQueues[i] = null;
    s_playerLoopRunners[i] = null;
}

// Reiniciar outros subsistemas
ValkarnTask.ResetStatics();
TimeProvider.ResetToDefault();   // reverte para UnityTimeProvider
PoolRegistry.Clear();
ContinuationQueue.ContinuationNode.ResetPool();
```

Quando o modo de jogo é subsequentemente inserido novamente, `Init()` dispara via `RuntimeInitializeOnLoadMethod` e novas filas e runners são alocados. A verificação de injeção do PlayerLoop (`HasValkarnTaskSystems`) evita que os sistemas marcadores sejam inseridos uma segunda vez se o domínio não foi totalmente descarregado.

## Escolhendo o timing correto

A maior parte do código deve usar o timing padrão `Update`. Recorra a outros apenas quando tiver uma razão específica.

| Timing | Quando usá-lo |
|---|---|
| `Initialization` / `LastInitialization` | Configuração muito inicial do frame; raramente necessário em código de jogo |
| `EarlyUpdate` / `LastEarlyUpdate` | Amostragem de entrada; executa antes da física e antes de `Update` |
| `FixedUpdate` / `LastFixedUpdate` | Lógica sincronizada com física; corresponde ao cadência de `MonoBehaviour.FixedUpdate` |
| `PreUpdate` / `LastPreUpdate` | Antes do despacho de atualização principal do Unity; útil para pré-passagem do scheduler personalizado |
| **`Update`** (padrão) | **Lógica de jogo padrão; corresponde a `MonoBehaviour.Update`** |
| `LastUpdate` | Lógica que deve ser executada após todas as chamadas `MonoBehaviour.Update` |
| `PreLateUpdate` / `LastPreLateUpdate` | Corresponde a `MonoBehaviour.LateUpdate`; seguimento de câmera e transform |
| `PostLateUpdate` / `LastPostLateUpdate` | Após o envio de renderização; finalização de UI, captura de screenshot |
| `TimeUpdate` / `LastTimeUpdate` | Sincronização de valores de tempo; raramente necessário |

```csharp
// Retomar no próximo tick do Update (padrão)
await ValkarnTask.Yield();

// Retomar no início do próximo FixedUpdate (seguro para física)
await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);

// Aguardar 2 segundos, avançando com tempo não escalado, verificado em LateUpdate
await ValkarnTask.Delay(
    TimeSpan.FromSeconds(2),
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.PreLateUpdate);

// Aguardar até que uma condição seja verdadeira, verificada após todas as chamadas LateUpdate
await ValkarnTask.WaitUntil(() => _ready, PlayerLoopTiming.LastPreLateUpdate);
```
