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
- Emgu CV 4.12
- Windows: ничего дополнительно не нужно — `Emgu.CV.runtime.windows` кладёт `cvextern.dll` рядом с бинарником сам.
- Linux: см. раздел ниже. Просто добавить пакет **недостаточно**.

---

## 🐧 Развёртывание на Linux

Библиотека вызывает OpenCV через нативную `libcvextern.so`. Если она не попадёт
в образ и не будет найдена загрузчиком, приложение падает на первом же вызове:

```
System.TypeInitializationException: The type initializer for 'Emgu.CV.CvInvoke' threw an exception.
 ---> System.DllNotFoundException: Unable to load shared library 'cvextern' or one of its dependencies.
```

Нужны три вещи одновременно.

### 1. База — Ubuntu 24.04 или новее

Сборка `Emgu.CV.runtime.ubuntu-x64` 4.12 требует `GLIBC_2.38` и `GLIBCXX_3.4.32`.
На Debian 12 и Ubuntu 22.04 их нет — `.so` не загрузится даже лёжа рядом с бинарником.
Alpine (musl) не подходит в принципе.

### 2. Системные зависимости Emgu

`libcvextern.so` тянет за собой geotiff, freetype, ffmpeg 6.x, gtk-3, hdf5, vtk и другое.
Официального скрипта Emgu **недостаточно** — без hdf5 загрузка падает на
`libhdf5_serial.so.103: cannot open shared object file`. Нужен и список пакетов, и скрипт:

```bash
apt-get update && apt-get install -y wget ca-certificates libgdiplus libx11-dev libgeotiff-dev libxt6t64 libusb-1.0-0 ffmpeg libhdf5-serial-dev libgstreamer-plugins-base1.0-0 libv4l-dev libvtk9-qt-dev

wget https://github.com/emgucv/emgucv/raw/4.12.0/platforms/ubuntu/24.04/apt_install_dependency
chmod +x apt_install_dependency && ./apt_install_dependency
```

На Ubuntu 22.04 и раньше пакет назывался `libxt6` — в 24.04 он переименован в `libxt6t64`.

### 3. Сама `libcvextern.so` должна попасть в вывод publish

Здесь главная ловушка. `Emgu.CV.runtime.ubuntu-x64` публикует нативку под
distro-specific RID `ubuntu-x64`, который в .NET 8+ по умолчанию не разворачивается
(предупреждение `NETSDK1206`). Что происходит на практике:

| Команда сборки | `libcvextern.so` в выводе |
|---|---|
| `dotnet publish` (без `-r`) | ✅ `runtimes/ubuntu-x64/native/libcvextern.so` |
| `dotnet publish -r linux-x64` | ❌ **ни одного файла** |
| `dotnet publish -r ubuntu-x64` | ❌ ошибка `NETSDK1083`, RID не распознаётся SDK |

`<UseRidGraph>true</UseRidGraph>` в проекте-потребителе только убирает предупреждение
`NETSDK1206`. На portable publish оно не влияет (нативка кладётся и без него),
а при `-r linux-x64` не помогает — ресурсы `ubuntu-x64` всё равно не попадут в вывод,
потому что `ubuntu-x64` не является предком `linux-x64` в графе RID.

**Вывод: публикуйте без `-r`.** Тогда нативка сама кладётся в
`runtimes/ubuntu-x64/native/`, и хост .NET находит её там — копировать ничего не нужно
и добавлять свойства в csproj тоже. Работает и на официальных образах
`mcr.microsoft.com/dotnet/*` (RID хоста `linux-x64`), и на .NET из репозитория Ubuntu
(RID `ubuntu.24.04-x64`). Если `-r linux-x64` всё-таки нужен — см. раздел ниже.

### Рекомендуемый Dockerfile

Publish **без** `-r` — нативка сама попадает в `runtimes/ubuntu-x64/native/`,
копировать ничего не нужно:

```dockerfile
# ===== Сборка =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY *.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

# ===== Runtime: тег -noble = Ubuntu 24.04 =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble AS final
ENV DEBIAN_FRONTEND=noninteractive LC_ALL=C.UTF-8 LANG=C.UTF-8

RUN apt-get update && apt-get install -y wget ca-certificates libgdiplus libx11-dev libgeotiff-dev libxt6t64 libusb-1.0-0 ffmpeg libhdf5-serial-dev libgstreamer-plugins-base1.0-0 libv4l-dev libvtk9-qt-dev && rm -rf /var/lib/apt/lists/*

WORKDIR /tmp/emgu-deps
RUN wget -q https://github.com/emgucv/emgucv/raw/4.12.0/platforms/ubuntu/24.04/apt_install_dependency && chmod +x apt_install_dependency && ./apt_install_dependency && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "/app/YourApp.dll"]
```

### Если нужен self-contained или `-r linux-x64`

Тогда нативку придётся доставить руками — в вывод publish она не попадёт:

```dockerfile
RUN dotnet publish -c Release -r linux-x64 --self-contained true -o /app/publish
RUN cp "$(find /root/.nuget/packages/emgu.cv.runtime.ubuntu-x64 -name libcvextern.so | head -n1)" /app/publish/libcvextern.so
```

и в финальном образе указать загрузчику, где искать:

```dockerfile
ENV LD_LIBRARY_PATH=/app
```

### Поддерживаемые платформы

| Платформа | Статус |
|---|---|
| Windows x64 | ✅ работает из коробки, ничего настраивать не надо |
| Windows x86 / ARM64 | ✅ нативка входит в `Emgu.CV.runtime.windows` |
| Linux x64, Ubuntu 24.04+ | ✅ при выполненных шагах 1–3 |
| Linux x64, Debian 12 / Ubuntu 22.04 и старее | ❌ нет `GLIBC_2.38` / `GLIBCXX_3.4.32` |
| Linux ARM64 | ❌ в зависимостях пакета нет нативки под ARM |
| Alpine, musl | ❌ |
| macOS | ❌ инициализатор сборки бросает `PlatformNotSupportedException` |

### Что нельзя

- **Публиковать с `-r linux-x64` (в том числе self-contained) и не копировать `.so`.**
  Сборка пройдёт без ошибок, образ соберётся, упадёт только в рантайме на первом сравнении.
- **Чистить папку `runtimes/` из вывода publish.** При publish без `-r` это единственная
  копия нативной библиотеки; удалив её «ради размера», вы получите `DllNotFoundException`.
- **Брать Alpine или Debian-базу.** `mcr.microsoft.com/dotnet/aspnet:8.0` — это Debian,
  нужен тег `-noble`.
- **Рассчитывать, что apt-зависимостей не нужно.** Их отсутствие даёт ровно то же
  сообщение об ошибке, что и отсутствие самого файла.
- **Менять свойства экземпляра, пока другие потоки им пользуются.** Настраивайте до начала работы.

### Что можно

- **Держать один `ImageHomographyComparer` на всё приложение и звать из нескольких потоков.**
  Общего изменяемого состояния между вызовами нет: ORB и матчер создаются внутри каждого
  вызова. Проверено — 64 параллельных вызова на одном экземпляре дают результат,
  идентичный последовательному.
- **Скармливать готовые `Mat` через `CompareMats`** — сэкономите JPEG-декод, если картинка
  уже в памяти. Принимаются и цветные, и grayscale.
- **Свободно менять параметры** (`TargetLongSide`, `NumberOfFeatures`, `LoweRatio` и прочие)
  до начала работы.
- **Подключать другие рантайм-пакеты Emgu** (`Emgu.CV.runtime.*`) под нужную вам платформу —
  библиотека работает через управляемое API Emgu и к конкретному рантайм-пакету не привязана.

### Диагностика

Текст `DllNotFoundException` обманчив: «отсутствует файл» и «файл есть, но не хватает
системных пакетов» дают **одинаковое** сообщение `cannot open shared object file`.
Иногда в списке всплывает имя недостающей зависимости (`libgeotiff.so.5`,
`libhdf5_serial.so.103`), но полагаться на это нельзя. Проверяйте по шагам:

```bash
# 1. файл вообще доехал? (шаг 3)
find /app -name libcvextern.so

# 2. чего не хватает в системе? (шаг 2 — пусто = всё на месте)
ldd $(find /app -name libcvextern.so | head -n1) | grep -i "not found"

# 3. та ли база? (шаг 1)
ldd $(find /app -name libcvextern.so | head -n1) 2>&1 | grep -i "GLIBC\|GLIBCXX"
```

Всё в этом разделе проверено на `ImageSimilarity 1.0.2` + `Emgu.CV 4.12.0.5764` + .NET 8,
на образах `mcr.microsoft.com/dotnet/*:8.0-noble` и `ubuntu:24.04`.

---

## ⭐ Поддержка

Если хочешь добавить новые функции или предложить улучшения — открывай issue или Pull Request.
