---
sidebar_position: 7
title: ValkarnSemaphore
---

# ValkarnSemaphore

`ValkarnSemaphore` एक शून्य-आवंटन असंक्रामक सेमाफोर है जो `ValkarnTask` पर निर्मित है। यह `SemaphoreSlim` की तरह काम करता है लेकिन Valkarn Tasks पाइपलाइन के साथ नेटिव रूप से एकीकृत होता है — कोई `Task` आवंटन नहीं, स्थिर अवस्था में GC दबाव नहीं।

```csharp
namespace UnaPartidaMas.Valkarn.Tasks
```

**तीव्र पथ (अप्रतिस्पर्धी):** तुरंत `ValkarnTask.CompletedTask` लौटाता है — शून्य आवंटन।
**धीमा पथ (प्रतिस्पर्धी):** वेटर नोड्स एक सीमित पूल से उधार लिए जाते हैं और पूर्ण होने पर वापस लौटाए जाते हैं — स्थिर अवस्था में GC शून्य।

---

## कन्स्ट्रक्टर

```csharp
public ValkarnSemaphore(int initialCount, int maxCount)
```

| पैरामीटर | विवरण |
|-----------|-------------|
| `initialCount` | तुरंत उपलब्ध स्लॉट की संख्या। `[0, maxCount]` में होनी चाहिए। |
| `maxCount` | स्लॉट गिनती की ऊपरी सीमा। > 0 होनी चाहिए। |

---

## गुण

```csharp
public int CurrentCount { get; } // अभी उपलब्ध स्लॉट (थ्रेड-सुरक्षित)
```

---

## विधियाँ

### WaitAsync

```csharp
public ValkarnTask WaitAsync(CancellationToken ct = default)
```

एक स्लॉट को असंक्रामक रूप से प्राप्त करता है। यदि स्लॉट उपलब्ध है तो तुरंत एक पूर्ण `ValkarnTask` लौटाता है (शून्य आवंटन)। यदि कोई स्लॉट उपलब्ध नहीं है तो कॉलर को `Release()` कॉल होने तक निलंबित करता है। वेटर FIFO क्रम में सेवित होते हैं।

यदि प्रतीक्षा के दौरान `ct` सक्रिय होता है तो `OperationCanceledException` फेंकता है।

### Wait

```csharp
public void Wait(CancellationToken ct = default)
```

समकालिक संस्करण। यदि कोई स्लॉट उपलब्ध नहीं है तो कॉलिंग थ्रेड को ब्लॉक करता है। केवल तब उपयोग करें जब आप `await` का उपयोग नहीं कर सकते (जैसे गैर-async एंट्री पॉइंट)।

### Release

```csharp
public void Release(int releaseCount = 1)
```

`releaseCount` स्लॉट लौटाता है। लंबित वेटर आंतरिक लॉक के बाहर FIFO क्रम में पूर्ण किए जाते हैं — निरंतरताएँ कभी भी लॉक होल्ड के साथ नहीं चलतीं।

यदि रिलीज़ `maxCount` से अधिक हो जाती तो `InvalidOperationException` फेंकता है।

### Dispose

```csharp
public void Dispose()
```

सभी लंबित वेटर को रद्द करता है और संसाधन मुक्त करता है। Idempotent।

---

## उपयोग

### पारस्परिक बहिष्करण (1 स्लॉट)

```csharp
var sem = new ValkarnSemaphore(initialCount: 1, maxCount: 1);

async ValkarnTask WriteFileAsync(string path, string content, CancellationToken ct)
{
    await sem.WaitAsync(ct);
    try
    {
        await File.WriteAllTextAsync(path, content, ct);
    }
    finally
    {
        sem.Release();
    }
}
```

### सीमित समवर्तिता (N स्लॉट)

```csharp
// अधिकतम 4 समवर्ती नेटवर्क अनुरोधों की अनुमति दें।
var sem = new ValkarnSemaphore(initialCount: 4, maxCount: 4);

async ValkarnTask FetchAsync(string url, CancellationToken ct)
{
    await sem.WaitAsync(ct);
    try
    {
        await DownloadAsync(url, ct);
    }
    finally
    {
        sem.Release();
    }
}
```

### उत्पादक / उपभोक्ता गेट

```csharp
// 0 स्लॉट से शुरू — उपभोक्ता तब तक प्रतीक्षा करता है जब तक उत्पादक संकेत न दे।
var gate = new ValkarnSemaphore(initialCount: 0, maxCount: 1);

async ValkarnTask ProducerAsync(CancellationToken ct)
{
    await ProduceDataAsync(ct);
    gate.Release(); // उपभोक्ता को संकेत दें
}

async ValkarnTask ConsumerAsync(CancellationToken ct)
{
    await gate.WaitAsync(ct); // संकेत की प्रतीक्षा करें
    await ConsumeDataAsync(ct);
}
```

---

## थ्रेड सुरक्षा

सभी ऑपरेशन थ्रेड-सुरक्षित हैं। `Release()` को किसी भी थ्रेड से कॉल किया जा सकता है, जिसमें बैकग्राउंड थ्रेड भी शामिल हैं। पूर्णताएँ आंतरिक लॉक के बाहर भेजी जाती हैं — एक निरंतरता जो सेमाफोर में पुनः प्रवेश करती है वह डेडलॉक नहीं करेगी।

---

## SemaphoreSlim के साथ तुलना

| | `ValkarnSemaphore` | `SemaphoreSlim` |
|---|---|---|
| तीव्र पथ आवंटन | शून्य | शून्य |
| धीमा पथ आवंटन | पूल नोड, GC शून्य | `Task` आवंटन |
| ValkarnTask के साथ एकीकरण | हाँ | `.AsValkarnTask()` आवश्यक |
| `IDisposable` | हाँ | हाँ |
| FIFO क्रम | हाँ | हाँ |
| रद्दीकरण | हाँ | हाँ |
