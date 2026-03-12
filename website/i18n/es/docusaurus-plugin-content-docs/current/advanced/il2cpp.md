---
sidebar_position: 2
title: Compatibilidad con IL2CPP
---

# Compatibilidad con IL2CPP

Valkarn Tasks está diseñado para funcionar correctamente bajo IL2CPP desde el principio. Esta página describe cada medida tomada y lo que necesitas hacer — o no hacer — para publicar de forma segura en plataformas IL2CPP como iOS, WebGL y consolas.

---

## Por Qué IL2CPP Requiere Cuidado Especial

IL2CPP convierte el IL de C# a código fuente C++ y luego lo compila con un compilador nativo. Dos características del pipeline son relevantes para una biblioteca async:

1. **Eliminación de código.** El eliminador de código gestionado de Unity (usando el enlazador de IL2CPP) elimina tipos, métodos y campos que no están referenciados por un grafo de llamadas analizable estáticamente. Los tipos accedidos solo a través de despacho de interfaz, compartición genérica o reflexión — lo que incluye clases de promesas agrupadas e implementaciones de `ISource` — pueden ser eliminados silenciosamente.

2. **Compartición genérica.** IL2CPP no genera un binario nativo separado para cada instanciación genérica. En su lugar, comparte código entre tipos de referencia y usa instanciaciones específicas para tipos de valor. Esto puede ocultar errores en desarrollo (Mono) que solo aparecen en compilaciones IL2CPP.

---

## El Archivo link.xml

La defensa principal contra la eliminación es el archivo `link.xml`, ubicado en:

```
Runtime/link.xml
```

Su contenido:

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

`preserve="all"` indica al enlazador que conserve cada tipo, método, campo y constructor en esos ensamblados independientemente de lo que encuentre el análisis estático. Esta es la configuración más segura para una biblioteca cuyos tipos internos se acceden a través de parámetros genéricos que el eliminador no puede rastrear.

Unity descubre y aplica los archivos `link.xml` automáticamente cuando se colocan dentro de una carpeta de paquete importada al proyecto. No se requiere ningún paso manual.

Si estás **bifurcando** o **embebiendo** el código fuente en lugar de usar el paquete, copia `link.xml` a una carpeta adyacente a `Resources` o colócalo en cualquier lugar donde Unity lo encuentre según la [documentación de Eliminación de Código Gestionado de Unity](https://docs.unity3d.com/Manual/ManagedCodeStripping.html).

---

## Tipos Internos Que Serían Eliminados Sin link.xml

Las siguientes categorías de tipos internos son el principal riesgo de eliminación:

### Clases de Promesas Agrupadas (Implementaciones de ISource)

Cada combinador y tipo de retraso crea una clase de promesa agrupada que implementa `ValkarnTask.ISource` o `ValkarnTask.ISource<T>`:

- `AsyncValkarnTaskRunner<TStateMachine>` — el runner de máquina de estados agrupado, uno por método async (especializado en `TStateMachine`)
- `WhenAllPromise<T1, T2>`, `WhenAllArrayPromise<T>`, `WhenAllVoidPromise2`, `WhenAllVoidPromise3`, `WhenAllVoidArrayPromise`
- `DeltaTimeDelayPromise`, `UnscaledDeltaTimeDelayPromise`, `RealtimeDelayPromise`
- `CanceledSource`, `CanceledSource<T>`, `ExceptionSource`, `ExceptionSource<T>`, `NeverSource`
- Internos de canales: `BoundedChannel<T>`, `UnboundedChannel<T>`, y sus tipos de lector/escritor

Estos tipos se instancian mediante métodos de fábrica genéricos (`ValkarnTaskPool<T>.GetOrCreate`). El grafo de llamadas del análisis estático comienza en una llamada a método genérico y no puede rastrear de forma fiable todas las instanciaciones concretas de `T`, por lo que sin `link.xml` cualquiera de estos tipos puede ser eliminado.

### ValkarnTaskPool\<T\>

```csharp
internal sealed class ValkarnTaskPool<T> : IPoolInfo where T : class, IPoolNode<T>
```

El grupo es genérico sobre su tipo de elemento. Cada clase de promesa tiene su propio campo de grupo estático. Si un tipo de promesa dado no se usa en una escena, el grupo y la clase de promesa pueden ser eliminados juntos.

### ValkarnTaskCompletionCore\<T\>

El núcleo de completado interno es un tipo de valor usado dentro de cada promesa. Contiene el callback de continuación, el token y el estado de completado. Nunca es referenciado por nombre desde fuera de la biblioteca.

---

## Restricciones de Tipos Genéricos e IL2CPP

El constructor de método async personalizado es genérico sobre el tipo de máquina de estados:

```csharp
public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
    ref TAwaiter awaiter, ref TStateMachine stateMachine)
    where TAwaiter : ICriticalNotifyCompletion
    where TStateMachine : IAsyncStateMachine
```

Y el runner es genérico sobre la máquina de estados:

```csharp
internal sealed class AsyncValkarnTaskRunner<TStateMachine>
    : IStateMachineRunnerPromise, IPoolNode<AsyncValkarnTaskRunner<TStateMachine>>
    where TStateMachine : IAsyncStateMachine
```

Bajo IL2CPP, se genera un nuevo tipo concreto para cada `TStateMachine` distinto (porque las máquinas de estados son structs, que requieren especialización genérica completa). Esto significa:

- Cada método `async ValkarnTask` en tu proyecto produce un tipo nativo `AsyncValkarnTaskRunner<TYourStateMachine>` separado.
- Si el eliminador quita `AsyncValkarnTaskRunner<T>` antes de ver todas las instanciaciones, algunos métodos async pueden fallar en tiempo de ejecución.
- `preserve="all"` en `link.xml` previene esto.

---

## Nivel de Eliminación de Código Gestionado

El nivel de eliminación de Unity se configura en **Player Settings → Other Settings → Managed Stripping Level**.

| Nivel       | Estado con Valkarn Tasks                                                        |
|-------------|---------------------------------------------------------------------------------|
| Disabled    | Seguro. No ocurre eliminación.                                                  |
| Low         | Seguro. Solo elimina ensamblados claramente no usados.                          |
| Medium      | Seguro **con `link.xml`**. El `link.xml` incluido preserva todos los tipos de tiempo de ejecución. |
| High        | Seguro **con `link.xml`**. La misma cobertura; `link.xml` es la capa de protección. |

Todos los niveles de eliminación hasta **High** inclusive son seguros siempre que el archivo `link.xml` esté presente y aplicado. No elimines ni modifiques `link.xml` sin entender qué tipos internos estás exponiendo al eliminador.

---

## Uso del Atributo [Preserve]

Una búsqueda del código fuente de Runtime no encuentra ningún atributo `[UnityEngine.Scripting.Preserve]` aplicado a miembros individuales. El enfoque elegido es la preservación a nivel de ensamblado mediante `link.xml` en lugar de atributos por miembro. Esto es intencional:

- `[Preserve]` por miembro requiere anotar cada clase agrupada individualmente, incluidas todas las adiciones futuras.
- `preserve="all"` a nivel de ensamblado en `link.xml` es más simple, menos propenso a errores, y garantizado para cubrir tipos añadidos en versiones futuras.

Si necesitas integrar Valkarn Tasks en un archivo `link.xml` más grande en lugar de usar el incluido con el paquete, la directiva equivalente es:

```xml
<assembly fullname="UnaPartidaMas.Valkarn.Tasks" preserve="all"/>
```

---

## Diseño de Grupo Orientado a IL2CPP

El grupo de objetos (`ValkarnTaskPool<T>`) contiene una nota explícita en su comentario de documentación:

> IL2CPP-first: main thread operations use zero atomics.

El grupo usa dos rutas de acceso:

- **Ruta rápida (hilo principal):** Un único campo `fastItem` se lee/escribe con acceso de campo simple — sin operaciones `Volatile` o `Interlocked`. Esto evita la sobrecarga de operaciones atómicas en el hilo principal, donde IL2CPP no siempre puede optimizarlas.
- **Ruta de desbordamiento (cualquier hilo):** Una pila lock-free de Treiber con `Interlocked.CompareExchange` para corrección bajo acceso concurrente desde hilos en segundo plano (por ejemplo, `ValkarnTask.RunOnThreadPool`).

La identidad del hilo principal se almacena en un campo `volatile int` en `ValkarnTaskPoolShared.MainThreadId`, publicado una vez al inicio mediante `RuntimeInitializeOnLoadMethod`. IL2CPP maneja correctamente los campos `volatile`.

---

## Consideraciones para Plataformas de Consola

Las plataformas de consola (PlayStation, Xbox, Nintendo Switch) usan IL2CPP exclusivamente. Lo siguiente aplica:

- La cobertura de `link.xml` es la misma que para otros objetivos IL2CPP. No se necesita preservación adicional específica de consola.
- `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` se dispara correctamente en todas las plataformas que Unity soporta para certificación de consola.
- Las operaciones `Interlocked` y `Volatile` están soportadas en todos los objetivos IL2CPP de consola. La pila Treiber del grupo es segura.
- La compartición genérica para instanciaciones de struct `TStateMachine` aplica en consolas. Cada método `async ValkarnTask` genera su propio tipo nativo — este es el comportamiento esperado y se maneja correctamente.
- Si una plataforma de consola impone requisitos AOT adicionales, confirma que tu `link.xml` se recoge correctamente comprobando el registro de compilación para "Stripping assembly: UnaPartidaMas.Valkarn.Tasks". Si el ensamblado es eliminado, el `link.xml` no fue descubierto.

---

## Verificar Que Nada Fue Eliminado

Después de compilar para un objetivo IL2CPP:

1. **Revisa el registro de compilación.** Unity imprime decisiones de eliminación. Busca `UnaPartidaMas.Valkarn.Tasks`. Si ves mensajes `Stripping class` para tipos internos de Valkarn, el `link.xml` no fue aplicado.

2. **Ejecuta una prueba de humo.** Una prueba mínima que ejercite `ValkarnTask.Delay`, `WhenAll` y un `ValkarnTaskCompletionSource` personalizado instanciará los tipos eliminados con más frecuencia. Si esos tres escenarios superan la compilación, el núcleo está intacto.

3. **Habilita "Strip Engine Code" de forma selectiva.** Si debes usar eliminación High y no puedes usar el `link.xml` incluido, habilita la eliminación de forma incremental y ejecuta pruebas después de cada aumento en el nivel de eliminación.

4. **Compilación IL2CPP con Development Build habilitado.** Las compilaciones de desarrollo incluyen diagnósticos adicionales. Si falta un tipo, el runtime reportará `TypeInitializationException` o una referencia nula en el sitio de llamada del tipo faltante. Relaciona la traza de pila con los tipos listados en la sección "Tipos Internos" anterior.

---

## Lista de Verificación Resumida

- `Runtime/link.xml` está presente en el paquete — verifica que no fue eliminado accidentalmente.
- El nivel de eliminación gestionado puede configurarse en cualquier nivel; High está soportado.
- No se necesita añadir atributos `[Preserve]` al código de la aplicación.
- No se necesitan atributos de sugerencia AOT ni llamadas de registro de código.
- Las compilaciones de consola usan el mismo `link.xml`; no se requieren adiciones específicas de plataforma.
- Si bifurcas el código fuente, lleva `link.xml` contigo.
