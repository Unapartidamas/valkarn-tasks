---
sidebar_position: 2
title: PlayerLoopTiming
---

# PlayerLoopTiming

```csharp
public enum PlayerLoopTiming : byte
```

Namespace: `UnaPartidaMas.Valkarn.Tasks`

Especifica qual fase do Unity PlayerLoop uma operação do ValkarnTasks usa para retomar após suspensão. Passar um valor `PlayerLoopTiming` controla **quando no frame** sua continuação de `await` é executada e **quando** itens recorrentes como delays e condições de espera são verificados.

Os valores do enum correspondem exatamente ao enum `PlayerLoopTiming` do UniTask, o que simplifica a migração do UniTask.

---

## Padrão

`PlayerLoopTiming.Update` (valor `8`) é o padrão para todas as operações. Se você não passar um argumento de timing, `Update` é usado.

---

## Valores

### Initialization

```
Valor: 0
Fase pai: UnityEngine.PlayerLoop.Initialization
Posição: primeiro subsistema na fase
```

Executado no início da fase de Initialization, antes dos próprios subsistemas de inicialização do Unity nessa fase. Dispara uma vez por frame, antes de `EarlyUpdate`. É raramente útil para código de gameplay, mas pode ser relevante para sistemas que devem ler ou configurar estado antes que qualquer outra coisa no frame o toque.

---

### LastInitialization

```
Valor: 1
Fase pai: UnityEngine.PlayerLoop.Initialization
Posição: último subsistema na fase
```

Executado no final da fase de Initialization, após os subsistemas de inicialização integrados do Unity. Prefira isso em vez de `Initialization` se precisar de timing da fase de inicialização, mas quiser deixar os próprios sistemas do Unity executarem primeiro.

---

### EarlyUpdate

```
Valor: 2
Fase pai: UnityEngine.PlayerLoop.EarlyUpdate
Posição: primeiro subsistema na fase
```

Executado no início de EarlyUpdate, antes que o Unity processe eventos de entrada e antes do passo de simulação de física. Este é o ponto mais antigo no frame onde os dados de entrada do frame anterior estão disponíveis. Útil para sistemas que devem amostrar entrada antes que qualquer código de gameplay seja executado.

---

### LastEarlyUpdate

```
Valor: 3
Fase pai: UnityEngine.PlayerLoop.EarlyUpdate
Posição: último subsistema na fase
```

Executado no final de EarlyUpdate, após os próprios subsistemas de early-update do Unity serem concluídos.

---

### FixedUpdate

```
Valor: 4
Fase pai: UnityEngine.PlayerLoop.FixedUpdate
Posição: primeiro subsistema na fase
```

Executado no início de cada passo de FixedUpdate. Isso corresponde ao timing de `MonoBehaviour.FixedUpdate()`. O Unity pode executar zero ou mais passos de FixedUpdate por frame renderizado dependendo de como o timestep de física se alinha ao delta time do frame.

Use este timing para qualquer coisa que deva permanecer sincronizada com a simulação de física: ler velocidades de `Rigidbody`, aplicar forças ou aguardar um número de passos determinado pela física.

```csharp
// Aguardar 10 passos de física
await VlkTask.DelayFrame(10, PlayerLoopTiming.FixedUpdate, ct);

// Aguardar até que uma condição de física seja verdadeira, verificada a cada passo de física
await VlkTask.WaitUntil(() => rb.velocity.magnitude < 0.1f,
    PlayerLoopTiming.FixedUpdate, ct);
```

---

### LastFixedUpdate

```
Valor: 5
Fase pai: UnityEngine.PlayerLoop.FixedUpdate
Posição: último subsistema na fase
```

Executado no final da fase FixedUpdate, após os subsistemas de física do Unity (incluindo `Physics.Simulate`) serem concluídos. Use isto para ler resultados de física após a simulação ter avançado.

---

### PreUpdate

```
Valor: 6
Fase pai: UnityEngine.PlayerLoop.PreUpdate
Posição: primeiro subsistema na fase
```

Executado no início da fase PreUpdate, que é executada após FixedUpdate e antes de Update. O Unity usa PreUpdate para tarefas como atualizações de zona de vento e processamento de eventos de rede.

---

### LastPreUpdate

```
Valor: 7
Fase pai: UnityEngine.PlayerLoop.PreUpdate
Posição: último subsistema na fase
```

Executado no final de PreUpdate.

---

### Update

```
Valor: 8  (padrão)
Fase pai: UnityEngine.PlayerLoop.Update
Posição: primeiro subsistema na fase
```

O **timing padrão** para todas as operações do ValkarnTasks. Executado no início da fase Update, que é onde `MonoBehaviour.Update()` dispara. Esta é a escolha certa para a grande maioria do código de gameplay.

```csharp
// Todos estes usam Update por padrão
await VlkTask.Yield();
await VlkTask.Delay(1000, ct);
await VlkTask.WaitUntil(() => _ready, ct: ct);
await VlkTask.NextFrame(ct: ct);
await VlkTask.DelayFrame(3, ct: ct);

// Explícito — idêntico ao acima
await VlkTask.Yield(PlayerLoopTiming.Update);
```

---

### LastUpdate

```
Valor: 9
Fase pai: UnityEngine.PlayerLoop.Update
Posição: último subsistema na fase
```

Executado no final da fase Update, após todas as chamadas `MonoBehaviour.Update()` e quaisquer outros subsistemas de Update serem concluídos. Use isto quando sua continuação deve observar os resultados dos métodos `Update()` de outros scripts — por exemplo, verificando um valor que outro script escreve durante `Update`.

```csharp
// Retomar após todas as chamadas MonoBehaviour.Update() neste frame
await VlkTask.Yield(PlayerLoopTiming.LastUpdate);
```

---

### PreLateUpdate

```
Valor: 10
Fase pai: UnityEngine.PlayerLoop.PreLateUpdate
Posição: primeiro subsistema na fase
```

Executado no início da fase PreLateUpdate, que é onde `MonoBehaviour.LateUpdate()` dispara. Use isto para seguimento de câmera, ajustes de transform e qualquer coisa que deva reagir às posições finais definidas durante `Update`.

```csharp
// Retomar no mesmo ponto que MonoBehaviour.LateUpdate()
await VlkTask.Yield(PlayerLoopTiming.PreLateUpdate);
```

---

### LastPreLateUpdate

```
Valor: 11
Fase pai: UnityEngine.PlayerLoop.PreLateUpdate
Posição: último subsistema na fase
```

Executado no final da fase PreLateUpdate, após todas as chamadas `MonoBehaviour.LateUpdate()`. Use isto quando precisar ler valores que scripts definem durante seu `LateUpdate`.

---

### PostLateUpdate

```
Valor: 12
Fase pai: UnityEngine.PlayerLoop.PostLateUpdate
Posição: primeiro subsistema na fase
```

Executado no início de PostLateUpdate. Esta fase dispara após a renderização ter sido enviada para o frame. O Unity a usa para tarefas como apresentar o frame buffer e limpar o estado de renderização. É também onde os callbacks `Camera.onPostRender` disparam.

Use este timing para operações de fim de frame: captura de screenshot, descarregamentos de nível por streaming ou qualquer trabalho que deva acontecer após a cena ter sido totalmente renderizada, mas antes do próximo frame começar.

---

### LastPostLateUpdate

```
Valor: 13
Fase pai: UnityEngine.PlayerLoop.PostLateUpdate
Posição: último subsistema na fase
```

O último ponto no loop de frame padrão antes que o Unity redefina o estado local do frame e inicie o próximo frame. Este é o final absoluto de um frame para propósitos de timing.

---

### TimeUpdate

```
Valor: 14
Fase pai: UnityEngine.PlayerLoop.TimeUpdate
Posição: primeiro subsistema na fase
```

Executado no início da fase TimeUpdate. O Unity usa esta fase para avançar estado relacionado ao tempo (como `Time.time`). Este timing dispara antes de `EarlyUpdate` em versões mais recentes do Unity.

Este timing raramente é necessário em código de jogo. Existe principalmente para sistemas que devem interceptar ou observar o avanço do tempo antes que qualquer outro sistema leia os novos valores de tempo.

---

### LastTimeUpdate

```
Valor: 15
Fase pai: UnityEngine.PlayerLoop.TimeUpdate
Posição: último subsistema na fase
```

Executado no final da fase TimeUpdate, após os subsistemas relacionados ao tempo do Unity serem concluídos.

---

## Tabela resumo

| Valor | Nome | Fase Unity | Posição | Callback MonoBehaviour comparável |
|---|---|---|---|---|
| 0 | `Initialization` | `Initialization` | Primeiro | — |
| 1 | `LastInitialization` | `Initialization` | Último | — |
| 2 | `EarlyUpdate` | `EarlyUpdate` | Primeiro | — |
| 3 | `LastEarlyUpdate` | `EarlyUpdate` | Último | — |
| 4 | `FixedUpdate` | `FixedUpdate` | Primeiro | `FixedUpdate()` |
| 5 | `LastFixedUpdate` | `FixedUpdate` | Último | após `FixedUpdate()` |
| 6 | `PreUpdate` | `PreUpdate` | Primeiro | — |
| 7 | `LastPreUpdate` | `PreUpdate` | Último | — |
| **8** | **`Update`** (padrão) | **`Update`** | **Primeiro** | **`Update()`** |
| 9 | `LastUpdate` | `Update` | Último | após todo `Update()` |
| 10 | `PreLateUpdate` | `PreLateUpdate` | Primeiro | `LateUpdate()` |
| 11 | `LastPreLateUpdate` | `PreLateUpdate` | Último | após todo `LateUpdate()` |
| 12 | `PostLateUpdate` | `PostLateUpdate` | Primeiro | após renderização |
| 13 | `LastPostLateUpdate` | `PostLateUpdate` | Último | fim do frame |
| 14 | `TimeUpdate` | `TimeUpdate` | Primeiro | — |
| 15 | `LastTimeUpdate` | `TimeUpdate` | Último | — |

---

## Quais APIs aceitam PlayerLoopTiming

Todas as APIs do ValkarnTasks baseadas em tempo e condição aceitam um parâmetro `PlayerLoopTiming` opcional. O padrão é sempre `Update`.

| Método | Assinatura |
|---|---|
| `VlkTask.Yield` | `Yield(PlayerLoopTiming timing = Update)` |
| `VlkTask.NextFrame` | `NextFrame(PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `VlkTask.DelayFrame` | `DelayFrame(int frameCount, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `VlkTask.Delay(int)` | `Delay(int ms, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `VlkTask.Delay(TimeSpan)` | `Delay(TimeSpan delay, DelayType type = DeltaTime, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `VlkTask.WaitUntil` | `WaitUntil(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |
| `VlkTask.WaitWhile` | `WaitWhile(Func<bool> predicate, PlayerLoopTiming timing = Update, CancellationToken ct = default)` |

---

## Exemplos de código

```csharp
using UnaPartidaMas.Valkarn.Tasks;
using System.Threading;

// Ceder para o próximo tick do Update (padrão)
await VlkTask.Yield();

// Ceder para o próximo tick do FixedUpdate — usar dentro de código impulsionado por física
await VlkTask.Yield(PlayerLoopTiming.FixedUpdate);

// Aguardar 500 ms usando deltaTime escalado, verificado a cada Update
await VlkTask.Delay(500, ct: destroyCancellationToken);

// Aguardar 500 ms usando deltaTime não escalado — não afetado por Time.timeScale
await VlkTask.Delay(
    500,
    DelayType.UnscaledDeltaTime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// Aguardar 500 ms usando tempo de relógio real (baseado em Stopwatch)
await VlkTask.Delay(
    500,
    DelayType.Realtime,
    PlayerLoopTiming.Update,
    destroyCancellationToken);

// Aguardar até que uma flag seja definida, verificada em LateUpdate
bool _isReady;
await VlkTask.WaitUntil(() => _isReady, PlayerLoopTiming.PreLateUpdate, destroyCancellationToken);

// Aguardar enquanto carregando, verificado no final do frame
await VlkTask.WaitWhile(() => _loading, PlayerLoopTiming.LastPostLateUpdate, destroyCancellationToken);

// Pular exatamente 5 passos de física
await VlkTask.DelayFrame(5, PlayerLoopTiming.FixedUpdate, destroyCancellationToken);

// Avançar um frame renderizado completo
await VlkTask.NextFrame(PlayerLoopTiming.Update, destroyCancellationToken);
```
