---
sidebar_position: 1
title: Instalación
---

# Instalación

## Requisitos

- **Unity 2023.1** o posterior
- **.NET Standard 2.1**

Paquetes opcionales (desbloquean características adicionales):

| Paquete | Versión | Característica |
|---------|---------|----------------|
| `com.unity.entities` | 1.0+ | Sistemas ECS asíncronos |
| `com.unity.burst` | 1.8+ | Programador Burst |
| `com.unity.collections` | 2.0+ | NativeTimerHeap |

---

## Vía Unity Package Manager (recomendado)

Agrega lo siguiente a tu `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.unapartidamas.valkarn.tasks": "https://github.com/unapartidamas/valkarn-tasks.git"
  }
}
```

O en el Editor de Unity: **Window → Package Manager → + → Add package from git URL**:

```
https://github.com/unapartidamas/valkarn-tasks.git
```

### Fijar a una versión específica

```json
"com.unapartidamas.valkarn.tasks": "https://github.com/unapartidamas/valkarn-tasks.git#v1.0.0"
```

---

## Verificar la instalación

Después de importar, abre **Window → Valkarn Tasks → Task Tracker**. Si la ventana se abre, el paquete está instalado correctamente.

También deberías ver las reglas del analizador activas — prueba escribir un método `async VlkTask` y olvida intencionalmente un `await`. El analizador lo marcará inmediatamente.
