---
sidebar_position: 1
title: इंस्टॉलेशन
---

# इंस्टॉलेशन

## आवश्यकताएँ

- **Unity 2023.1** या बाद का संस्करण
- **.NET Standard 2.1**

वैकल्पिक packages (अतिरिक्त features unlock करते हैं):

| Package | Version | Feature |
|---------|---------|---------|
| `com.unity.entities` | 1.0+ | ECS async systems |
| `com.unity.burst` | 1.8+ | Burst scheduler |
| `com.unity.collections` | 2.0+ | NativeTimerHeap |

---

## Unity Package Manager के माध्यम से (अनुशंसित)

अपने `Packages/manifest.json` में जोड़ें:

```json
{
  "dependencies": {
    "com.unapartidamas.valkarn.tasks": "https://github.com/unapartidamas/valkarn-tasks.git"
  }
}
```

या Unity Editor में: **Window → Package Manager → + → Add package from git URL**:

```
https://github.com/unapartidamas/valkarn-tasks.git
```

### किसी विशिष्ट version पर lock करें

```json
"com.unapartidamas.valkarn.tasks": "https://github.com/unapartidamas/valkarn-tasks.git#v1.0.0"
```

---

## इंस्टॉलेशन सत्यापित करें

Import करने के बाद, **Window → Valkarn Tasks → Task Tracker** खोलें। यदि window खुलती है, तो package सही ढंग से install है।

आपको analyzer नियम भी सक्रिय दिखने चाहिए — एक `async VlkTask` method लिखने का प्रयास करें और जानबूझकर `await` भूल जाएँ। analyzer तुरंत इसे flag करेगा।
