---
sidebar_position: 1
title: Installation
---

# Installation

## Prérequis

- **Unity 2023.1** ou version ultérieure
- **.NET Standard 2.1**

Packages optionnels (déverrouille des fonctionnalités supplémentaires) :

| Package | Version | Fonctionnalité |
|---------|---------|----------------|
| `com.unity.entities` | 1.0+ | Systèmes ECS asynchrones |
| `com.unity.burst` | 1.8+ | Planificateur Burst |
| `com.unity.collections` | 2.0+ | NativeTimerHeap |

---

## Via Unity Package Manager (recommandé)

Ajoutez à votre `Packages/manifest.json` :

```json
{
  "dependencies": {
    "com.unapartidamas.valkarn.tasks": "https://github.com/unapartidamas/valkarn-tasks.git"
  }
}
```

Ou dans l'éditeur Unity : **Window → Package Manager → + → Add package from git URL** :

```
https://github.com/unapartidamas/valkarn-tasks.git
```

### Verrouiller sur une version spécifique

```json
"com.unapartidamas.valkarn.tasks": "https://github.com/unapartidamas/valkarn-tasks.git#v1.0.0"
```

---

## Vérifier l'installation

Après l'importation, ouvrez **Window → Valkarn Tasks → Task Tracker**. Si la fenêtre s'ouvre, le package est installé correctement.

Vous devriez également voir les règles d'analyseur actives — essayez d'écrire une méthode `async VlkTask` et d'oublier intentionnellement un `await`. L'analyseur le signalera immédiatement.
