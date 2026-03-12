---
sidebar_position: 2
title: Compatibilidade com IL2CPP
---

# Compatibilidade com IL2CPP

O Valkarn Tasks foi projetado desde o início para funcionar corretamente sob IL2CPP. Esta página descreve cada medida adotada e o que você precisa fazer — ou não fazer — para lançar com segurança em plataformas IL2CPP como iOS, WebGL e consoles.

---

## Por que o IL2CPP requer cuidados especiais

O IL2CPP converte o IL do C# para código-fonte C++ e depois o compila com um compilador nativo. Dois aspectos do pipeline são relevantes para uma biblioteca async:

1. **Stripping de código.** O stripper de código gerenciado do Unity (usando o linker do IL2CPP) remove tipos, métodos e campos que não são referenciados por um grafo de chamadas analisável estaticamente. Tipos acessados apenas via despacho de interface, compartilhamento genérico ou reflexão — o que inclui classes de promise em pool e implementações de `ISource` — podem ser silenciosamente removidos.

2. **Compartilhamento genérico.** O IL2CPP não gera um binário nativo separado para cada instanciação genérica. Em vez disso, compartilha código entre tipos de referência e usa instanciações específicas para tipos de valor. Isso pode ocultar bugs em desenvolvimento (Mono) que só aparecem em builds IL2CPP.

---

## O arquivo link.xml

A principal defesa contra o stripping é o arquivo `link.xml`, localizado em:

```
Runtime/link.xml
```

Seu conteúdo:

```xml
<linker>
  <!-- Preserve all types in the Valkarn.Tasks runtime assembly.
       IL2CPP code stripping can remove internal types accessed only via
       interface dispatch, generic sharing, or reflection (e.g. promise
       classes, pooled runners, ISource implementations). -->
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks.Burst" preserve="all"/>
  <assembly fullname="UnaPartidaMas.Valkarn.Tasks.ECS" preserve="all"/>
</linker>
```

`preserve="all"` instrui o linker a manter todos os tipos, métodos, campos e construtores daquelas assemblies independentemente do que a análise estática encontrar. Esta é a configuração mais segura para uma biblioteca cujos tipos internos são acessados via parâmetros genéricos que o stripper não consegue rastrear.

O Unity descobre e aplica arquivos `link.xml` automaticamente quando são colocados dentro de uma pasta de pacote importada para o projeto. Nenhum passo manual é necessário.

Se você está fazendo **fork** ou **embutindo** o código-fonte em vez de usar o pacote, copie `link.xml` para uma pasta adjacente a `Resources` ou coloque-o em qualquer lugar que o Unity encontre de acordo com a [documentação de Managed Code Stripping do Unity](https://docs.unity3d.com/Manual/ManagedCodeStripping.html).

---

## Tipos internos que seriam removidos sem link.xml

As seguintes categorias de tipos internos são o principal risco de stripping:

### Classes de Promise em Pool (Implementações de ISource)

Todo combinador e tipo de delay cria uma classe de promise em pool que implementa `VlkTask.ISource` ou `VlkTask.ISource<T>`:

- `AsyncVlkTaskRunner<TStateMachine>` — o runner de máquina de estados em pool, um por método async (especializado em `TStateMachine`)
- `WhenAllPromise<T1, T2>`, `WhenAllArrayPromise<T>`, `WhenAllVoidPromise2`, `WhenAllVoidPromise3`, `WhenAllVoidArrayPromise`
- `DeltaTimeDelayPromise`, `UnscaledDeltaTimeDelayPromise`, `RealtimeDelayPromise`
- `CanceledSource`, `CanceledSource<T>`, `ExceptionSource`, `ExceptionSource<T>`, `NeverSource`
- Internos de Channel: `BoundedChannel<T>`, `UnboundedChannel<T>`, e seus tipos de reader/writer

Esses tipos são instanciados via métodos factory genéricos (`VlkTaskPool<T>.GetOrCreate`). O grafo de chamadas da análise estática começa em uma chamada de método genérico e não consegue rastrear de forma confiável todas as instanciações concretas de `T`, então sem `link.xml` qualquer um desses tipos pode ser removido.

### VlkTaskPool\<T\>

```csharp
internal sealed class VlkTaskPool<T> : IPoolInfo where T : class, IPoolNode<T>
```

O pool é genérico sobre seu tipo de elemento. Cada classe de promise tem seu próprio campo de pool estático. Se um determinado tipo de promise não for utilizado em uma cena, o pool e a classe de promise podem ser removidos juntos.

### VlkTaskCompletionCore\<T\>

O núcleo de conclusão interno é um tipo de valor usado dentro de cada promise. Ele armazena o callback de continuação, o token e o estado de conclusão. Nunca é referenciado pelo nome de fora da biblioteca.

---

## Restrições de tipo genérico e IL2CPP

O custom async method builder é genérico sobre o tipo de máquina de estados:

```csharp
public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
    ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine
```

E o runner é genérico sobre a máquina de estados:

```csharp
internal sealed class AsyncVlkTaskRunner<TStateMachine>
    : IStateMachineRunnerPromise, IPoolNode<AsyncVlkTaskRunner<TStateMachine>>
    where TStateMachine : IAsyncStateMachine
```

Sob IL2CPP, um novo tipo concreto é gerado para cada `TStateMachine` distinto (porque máquinas de estados são structs, que requerem especialização genérica completa). Isso significa:

- Cada método `async VlkTask` no seu projeto produz um tipo nativo `AsyncVlkTaskRunner<TYourStateMachine>` separado.
- Se o stripper remover `AsyncVlkTaskRunner<T>` antes de ver todas as instanciações, alguns métodos async podem falhar em runtime.
- `preserve="all"` no `link.xml` previne isso.

---

## Nível de Managed Code Stripping

O nível de stripping do Unity é configurado em **Player Settings → Other Settings → Managed Stripping Level**.

| Nível       | Status com Valkarn Tasks                                                                |
|-------------|----------------------------------------------------------------------------------------|
| Disabled    | Seguro. Nenhum stripping ocorre.                                                       |
| Low         | Seguro. Remove apenas assemblies claramente não utilizadas.                            |
| Medium      | Seguro **com `link.xml`**. O `link.xml` incluído preserva todos os tipos de runtime. |
| High        | Seguro **com `link.xml`**. Mesma cobertura; `link.xml` é a camada de proteção.       |

Todos os níveis de stripping até e incluindo **High** são seguros enquanto o arquivo `link.xml` estiver presente e aplicado. Não remova ou modifique `link.xml` sem entender quais tipos internos você está expondo ao stripper.

---

## Uso do atributo [Preserve]

Uma busca no código-fonte do Runtime não encontra nenhum atributo `[UnityEngine.Scripting.Preserve]` aplicado a membros individuais. A abordagem escolhida é a preservação a nível de assembly via `link.xml` em vez de atributos por membro. Isso é intencional:

- `[Preserve]` por membro requer anotar cada classe em pool individualmente, incluindo todas as adições futuras.
- `preserve="all"` a nível de assembly no `link.xml` é mais simples, menos propenso a erros e garantido para cobrir tipos adicionados em versões futuras.

Se você precisa integrar o Valkarn Tasks em um arquivo `link.xml` maior em vez de usar o bundled do pacote, a diretiva equivalente é:

```xml
<assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
```

---

## Design do Pool focado em IL2CPP

O object pool (`VlkTaskPool<T>`) contém uma nota explícita em seu comentário de documentação:

> IL2CPP-first: main thread operations use zero atomics.

O pool usa dois caminhos de acesso:

- **Caminho rápido (thread principal):** Um único campo `fastItem` é lido/escrito com acesso direto ao campo — sem operações `Volatile` ou `Interlocked`. Isso evita a sobrecarga de operações atômicas na thread principal, onde o IL2CPP nem sempre consegue otimizá-las.
- **Caminho de overflow (qualquer thread):** Uma pilha lock-free Treiber com `Interlocked.CompareExchange` para corretude sob acesso concorrente de threads em background (ex.: `VlkTask.RunOnThreadPool`).

A identidade da thread principal é armazenada em um campo `volatile int` em `VlkTaskPoolShared.MainThreadId`, publicado uma vez na inicialização via `RuntimeInitializeOnLoadMethod`. O IL2CPP lida corretamente com campos `volatile`.

---

## Considerações para plataformas de console

Plataformas de console (PlayStation, Xbox, Nintendo Switch) usam IL2CPP exclusivamente. O seguinte se aplica:

- A cobertura do `link.xml` é a mesma que para outros alvos IL2CPP. Nenhuma preservação adicional específica para console é necessária.
- `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` funciona corretamente em todas as plataformas que o Unity suporta para certificação de console.
- Operações `Interlocked` e `Volatile` são suportadas em todos os alvos IL2CPP de console. A pilha Treiber do pool é segura.
- O compartilhamento genérico para instanciações `TStateMachine` em struct se aplica em consoles. Cada método `async VlkTask` gera seu próprio tipo nativo — este é o comportamento esperado e é tratado corretamente.
- Se uma plataforma de console impuser requisitos AOT adicionais, confirme que seu `link.xml` foi detectado corretamente verificando o log de build em busca de "Stripping assembly: UnaPartidaMas.Valkarn.Tasks". Se a assembly for removida, o `link.xml` não foi descoberto.

---

## Verificando se nada foi removido

Após fazer build para um alvo IL2CPP:

1. **Verifique o log de build.** O Unity imprime decisões de stripping. Pesquise por `UnaPartidaMas.Valkarn.Tasks`. Se você ver mensagens `Stripping class` para tipos internos do Valkarn, o `link.xml` não foi aplicado.

2. **Execute um smoke test.** Um teste mínimo que exercita `VlkTask.Delay`, `WhenAll` e um `VlkTaskCompletionSource` customizado instanciará os tipos mais frequentemente removidos. Se esses três cenários sobreviverem ao build, o núcleo está intacto.

3. **Habilite "Strip Engine Code" seletivamente.** Se você deve usar stripping High e não pode usar o `link.xml` bundled, habilite o stripping incrementalmente e execute testes após cada aumento no nível de stripping.

4. **Build IL2CPP com Development Build habilitado.** Builds de desenvolvimento incluem diagnósticos adicionais. Se um tipo estiver ausente, o runtime reportará `TypeInitializationException` ou uma referência nula no site de chamada do tipo ausente. Associe o stack trace aos tipos listados na seção "Tipos internos" acima.

---

## Checklist de verificação

- `Runtime/link.xml` está presente no pacote — verifique se não foi acidentalmente deletado.
- O nível de managed stripping pode ser definido para qualquer nível; High é suportado.
- Nenhum atributo `[Preserve]` precisa ser adicionado ao código da aplicação.
- Nenhum atributo de hint AOT ou chamadas de registro de código são necessários.
- Builds de console usam o mesmo `link.xml`; nenhuma adição específica de plataforma é necessária.
- Se você fizer fork do código-fonte, leve o `link.xml` junto.
