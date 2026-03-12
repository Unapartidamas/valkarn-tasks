---
sidebar_position: 1
title: Installation
---

# Installation

## Requirements

- **Unity 2023.1** or later
- **.NET Standard 2.1**

Optional packages (unlocks additional features):

| Package | Version | Feature |
|---------|---------|---------|
| `com.unity.entities` | 1.0+ | ECS async systems |
| `com.unity.burst` | 1.8+ | Burst scheduler |
| `com.unity.collections` | 2.0+ | NativeTimerHeap |

---

## Via Unity Package Manager (recommended)

Add to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.unapartidamas.valkarn.tasks": "https://github.com/unapartidamas/valkarn-tasks.git"
  }
}
```

Or in the Unity Editor: **Window → Package Manager → + → Add package from git URL**:

```
https://github.com/unapartidamas/valkarn-tasks.git
```

### Lock to a specific version

```json
"com.unapartidamas.valkarn.tasks": "https://github.com/unapartidamas/valkarn-tasks.git#v1.0.0"
```

---

## Verify the installation

After importing, open **Window → Valkarn Tasks → Task Tracker**. If the window opens, the package is installed correctly.

You should also see the analyzer rules active — try writing an `async ValkarnTask` method and intentionally forgetting an `await`. The analyzer will flag it immediately.

