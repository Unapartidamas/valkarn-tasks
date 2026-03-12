---
sidebar_position: 1
title: التثبيت
---

# التثبيت

## المتطلبات

- **Unity 2023.1** أو أحدث
- **.NET Standard 2.1**

الحزم الاختيارية (تُفعّل ميزات إضافية):

| الحزمة | الإصدار | الميزة |
|---------|---------|---------|
| `com.unity.entities` | 1.0+ | أنظمة ECS غير متزامنة |
| `com.unity.burst` | 1.8+ | مجدول Burst |
| `com.unity.collections` | 2.0+ | NativeTimerHeap |

---

## عبر Unity Package Manager (موصى به)

أضف إلى ملف `Packages/manifest.json` الخاص بك:

```json
{
  "dependencies": {
    "com.unapartidamas.valkarn.tasks": "https://github.com/unapartidamas/valkarn-tasks.git"
  }
}
```

أو في محرر Unity: **Window → Package Manager → + → Add package from git URL**:

```
https://github.com/unapartidamas/valkarn-tasks.git
```

### التثبيت على إصدار محدد

```json
"com.unapartidamas.valkarn.tasks": "https://github.com/unapartidamas/valkarn-tasks.git#v1.0.0"
```

---

## التحقق من التثبيت

بعد الاستيراد، افتح **Window → Valkarn Tasks → Task Tracker**. إذا فُتحت النافذة، فالحزمة مثبتة بشكل صحيح.

يجب أن ترى أيضًا قواعد المحلل نشطة — جرّب كتابة طريقة `async ValkarnTask` ونسيان `await` عمدًا. سيُعلم المحلل عنها فورًا.
