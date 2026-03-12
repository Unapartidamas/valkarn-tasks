---
sidebar_position: 1
title: Instalação
---

# Instalação

## Requisitos

- **Unity 2023.1** ou superior
- **.NET Standard 2.1**

Pacotes opcionais (desbloqueiam funcionalidades adicionais):

| Pacote | Versão | Funcionalidade |
|---------|---------|---------|
| `com.unity.entities` | 1.0+ | Sistemas ECS assíncronos |
| `com.unity.burst` | 1.8+ | Scheduler Burst |
| `com.unity.collections` | 2.0+ | NativeTimerHeap |

---

## Via Unity Package Manager (recomendado)

Adicione ao seu `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.unapartidamas.valkarn.tasks": "https://github.com/unapartidamas/valkarn-tasks.git"
  }
}
```

Ou no Unity Editor: **Window → Package Manager → + → Add package from git URL**:

```
https://github.com/unapartidamas/valkarn-tasks.git
```

### Fixar em uma versão específica

```json
"com.unapartidamas.valkarn.tasks": "https://github.com/unapartidamas/valkarn-tasks.git#v1.0.0"
```

---

## Verificar a instalação

Após importar, abra **Window → Valkarn Tasks → Task Tracker**. Se a janela abrir, o pacote está instalado corretamente.

Você também deve ver as regras do analisador ativas — tente escrever um método `async ValkarnTask` e esqueça intencionalmente um `await`. O analisador irá sinalizá-lo imediatamente.
