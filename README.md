# ImageSimilarity

**ImageSimilarity** — .NET-библиотека для сравнения изображений по ключевым точкам (ORB) и гомографии (RANSAC) через Emgu CV.  
Позволяет определить, являются ли два изображения **одним и тем же исходником**, даже если они:

- кадрированы;
- уменьшены/увеличены;
- перекодированы в JPEG;
- изменены фильтрами;
- затемнены/осветлены;
- имеют текстовые подписи или графические оверлеи.

Библиотека возвращает числовую оценку сходства **0.0–1.0**, а также подробные метрики матчинга.

---

## ✨ Возможности

- Масштабирование обоих изображений **к единому размеру** для стабильности ORB.
- Детекция и сравнение ключевых точек ORB.
- Матчинг через **BFMatcher + KNN (k=2) + Lowe Ratio Test**.
- Строительство гомографии через **RANSAC**.
- Подсчёт:
  - количества матчей;
  - количества инлаеров;
  - доли инлаеров;
  - итогового `SimilarityScore`.
- Адаптивная формула скоринга:
  - душит ложные совпадения при малом количестве матчей (например, «олени»/«деревья»);
  - поддерживает высокие оценки для реальных дубликатов (даже с текстом и фильтрами).
- Подходит для бэкендов, микросервисов и контейнеров.

---

## 📦 Установка

```bash
dotnet add package ImageSimilarity
```

---

## 🚀 Быстрый старт

```csharp
using ImageSimilarity;

var comparer = new ImageHomographyComparer();

var result = comparer.CompareFiles("original.jpg", "modified.jpg");

Console.WriteLine($"Score:         {result.SimilarityScore:F3}");
Console.WriteLine($"Total matches: {result.TotalMatches}");
Console.WriteLine($"Inliers:       {result.InliersCount}");
Console.WriteLine($"Inlier ratio:  {result.InliersRatio:F3}");
Console.WriteLine($"Same origin:   {result.IsSameOrigin()}");
```

---

## 📊 Интерпретация SimilarityScore

Рекомендуемая шкала:

| Score | Значение |
|-------|----------|
| 0.00–0.10 | Сцены разные |
| 0.10–0.40 | Некоторая общая структура / фон |
| 0.40–0.70 | Тот же источник, но с сильными изменениями (текст, фильтр, кадрирование) |
| 0.70–1.00 | Очень похожие изображения, один и тот же исходник |

`IsSameOrigin()` по умолчанию использует пороги:

- `SimilarityScore >= 0.5`
- `InliersCount >= 15`

---

## 🧠 Как работает алгоритм

1. **Нормализация масштаба**
   - оба изображения приводятся к одной длине длинной стороны (TargetLongSide = 600).

2. **Преобразование в градации серого**

3. **ORB: детекция + дескрипторы**
   - количество фич: 1500;
   - масштабирующий фактор: 1.2;
   - уровни пирамиды: 8.

4. **KNN-matching (k=2) + Lowe Ratio Test**
   - отсеиваются слабые совпадения.

5. **Гомография через RANSAC**
   - формируется инлайер-маска.

6. **Адаптивный скоринг**
   - если матчей мало (<30) → score душится квадратично;
   - если много — учитывается качество (inlierRatio) + сила совпадения;
   - итог в диапазоне [0..1].

---

## 🧩 Параметры

```csharp
public int TargetLongSide { get; set; } = 600;
public int NumberOfFeatures { get; set; } = 1500;
public float ScaleFactor { get; set; } = 1.2f;
public int NLevels { get; set; } = 8;
public double LoweRatio { get; set; } = 0.75;
public double RansacReprojThreshold { get; set; } = 3.0;
```

Можно свободно настраивать.

---

## 📘 Пример интеграции в Minimal API

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = WebApplication.Create();

app.MapPost("/compare", async (HttpRequest request) =>
{
    var form = await request.ReadFormAsync();
    var source = form.Files.GetFile("source");
    var search = form.Files.GetFile("search");

    var temp1 = Path.GetTempFileName();
    var temp2 = Path.GetTempFileName();

    using (var s = File.Create(temp1)) await source.CopyToAsync(s);
    using (var s = File.Create(temp2)) await search.CopyToAsync(s);

    var comparer = new ImageHomographyComparer();
    var result = comparer.CompareFiles(temp1, temp2);

    return Results.Json(result);
});

app.Run();
```

---

## 📦 `HomographyResult`

```csharp
public class HomographyResult
{
    public int TotalMatches { get; set; }
    public int InliersCount { get; set; }
    public double InliersRatio { get; set; }
    public double SimilarityScore { get; set; }

    public bool IsSameOrigin(double scoreThreshold = 0.5, int minInliers = 15)
    {
        return SimilarityScore >= scoreThreshold && InliersCount >= minInliers;
    }
}
```

---

## 🛠 Требования

- .NET 8+  
- Emgu CV  
- В Linux-контейнерах нужны нативные OpenCV-библиотеки:
  - libopencv-core  
  - libopencv-imgproc  
  - libopencv-features2d  
  - libopencv-calib3d

---

## ⭐ Поддержка

Если хочешь добавить новые функции или предложить улучшения — открывай issue или Pull Request.
