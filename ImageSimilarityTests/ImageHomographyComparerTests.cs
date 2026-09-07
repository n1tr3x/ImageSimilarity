using System.Collections.Concurrent;
using Emgu.CV;
using Emgu.CV.CvEnum;
using ImageSimilarity;
using Xunit;

namespace ImageSimilarityTests;

/// <summary>
/// Набор фиксирует текущее поведение компаратора на эталонных парах из tests/.
///
/// Диапазоны намеренно широкие: OpenCV на Windows и на Linux даёт на одних и тех же
/// файлах немного разные числа (пара 2 — 0.956 против 0.970). Проверяем вердикт и
/// порядок величины, а не точное значение, иначе набор будет красным на половине машин.
///
/// Пары:
///   1 — два разных снимка оленьего стада (похожая сцена, разный кадр);
///   2 — один исходник, cmp уменьшен до 200x103;
///   3 — разные сцены;
///   4 — один исходник, cmp с красным фильтром и текстовым оверлеем.
/// </summary>
public class ImageHomographyComparerTests
{
    private static string Image(string pair, string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "tests", pair, fileName);

    private static string Orig(string pair) => Image(pair, "orig.jpg");

    private static string Cmp(string pair) => Image(pair, "cmp.jpg");

    private static ImageHomographyComparer Comparer() => new();

    [Fact]
    public void TestImagesAreCopiedToOutput()
    {
        foreach (var pair in new[] { "1", "2", "3", "4" })
        {
            Assert.True(File.Exists(Orig(pair)), $"нет файла {Orig(pair)}");
            Assert.True(File.Exists(Cmp(pair)), $"нет файла {Cmp(pair)}");
        }
    }

    [Theory]
    [InlineData("2")]
    [InlineData("4")]
    public void SameOriginPairIsDetected(string pair)
    {
        var result = Comparer().CompareFiles(Orig(pair), Cmp(pair));

        Assert.True(
            result.IsSameOrigin(),
            $"пара {pair}: ожидался вердикт «тот же исходник», получено " +
            $"score={result.SimilarityScore:F4}, inliers={result.InliersCount}");

        Assert.InRange(result.SimilarityScore, 0.6, 1.0);
        Assert.True(result.InliersCount >= 30, $"inliers={result.InliersCount}");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("3")]
    public void DifferentSceneIsRejected(string pair)
    {
        var result = Comparer().CompareFiles(Orig(pair), Cmp(pair));

        Assert.False(
            result.IsSameOrigin(),
            $"пара {pair}: ожидался отказ, получено " +
            $"score={result.SimilarityScore:F4}, inliers={result.InliersCount}");

        Assert.InRange(result.SimilarityScore, 0.0, 0.15);
    }

    [Theory]
    [InlineData("2", "orig.jpg", "3", "orig.jpg")]
    [InlineData("1", "orig.jpg", "2", "orig.jpg")]
    [InlineData("1", "cmp.jpg", "4", "cmp.jpg")]
    public void UnrelatedImagesAreRejected(string leftPair, string leftFile, string rightPair, string rightFile)
    {
        var result = Comparer().CompareFiles(Image(leftPair, leftFile), Image(rightPair, rightFile));

        Assert.False(result.IsSameOrigin(), $"score={result.SimilarityScore:F4}");
        Assert.InRange(result.SimilarityScore, 0.0, 0.15);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("4")]
    public void IdenticalFileScoresOne(string pair)
    {
        var result = Comparer().CompareFiles(Orig(pair), Orig(pair));

        Assert.Equal(1.0, result.SimilarityScore, 3);
        Assert.True(result.IsSameOrigin());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("4")]
    public void MetricsStayConsistent(string pair)
    {
        var result = Comparer().CompareFiles(Orig(pair), Cmp(pair));

        Assert.InRange(result.SimilarityScore, 0.0, 1.0);
        Assert.InRange(result.InliersRatio, 0.0, 1.0);
        Assert.True(result.InliersCount <= result.TotalMatches,
            $"inliers={result.InliersCount} > matches={result.TotalMatches}");
    }

    [Fact]
    public void ResultIsDeterministic()
    {
        var comparer = Comparer();

        var first = comparer.CompareFiles(Orig("4"), Cmp("4"));
        var second = comparer.CompareFiles(Orig("4"), Cmp("4"));

        Assert.Equal(first.SimilarityScore, second.SimilarityScore, 10);
        Assert.Equal(first.TotalMatches, second.TotalMatches);
        Assert.Equal(first.InliersCount, second.InliersCount);
    }

    /// <summary>
    /// Один экземпляр можно держать на всё приложение: общего изменяемого состояния
    /// между вызовами нет, ORB и матчер создаются внутри каждого вызова.
    /// </summary>
    [Fact]
    public void SingleInstanceIsSafeForConcurrentUse()
    {
        var comparer = Comparer();
        var expected = comparer.CompareFiles(Orig("2"), Cmp("2")).SimilarityScore;

        var results = new ConcurrentBag<double>();
        Parallel.For(0, 32, new ParallelOptions { MaxDegreeOfParallelism = 8 },
            _ => results.Add(comparer.CompareFiles(Orig("2"), Cmp("2")).SimilarityScore));

        Assert.Equal(32, results.Count);
        Assert.All(results, score => Assert.Equal(expected, score, 10));
    }

    [Fact]
    public void CompareMatsMatchesCompareFiles()
    {
        using var src = CvInvoke.Imread(Orig("4"), ImreadModes.Grayscale);
        using var dst = CvInvoke.Imread(Cmp("4"), ImreadModes.Grayscale);

        var fromMats = Comparer().CompareMats(src, dst);
        var fromFiles = Comparer().CompareFiles(Orig("4"), Cmp("4"));

        Assert.Equal(fromFiles.SimilarityScore, fromMats.SimilarityScore, 10);
        Assert.Equal(fromFiles.TotalMatches, fromMats.TotalMatches);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankPathThrowsArgumentException(string? path)
    {
        var comparer = Comparer();

        Assert.Throws<ArgumentException>(() => comparer.CompareFiles(path!, Cmp("1")));
        Assert.Throws<ArgumentException>(() => comparer.CompareFiles(Orig("1"), path!));
    }

    /// <summary>
    /// Фиксирует фактический контракт на плохой файл: Emgu бросает ArgumentException
    /// прямо в Imread — и на отсутствующем файле ("File ... do not exist"), и на
    /// нечитаемом ("Unable to decode file"). До проверок IsEmpty в CompareFiles
    /// управление не доходит, поэтому собственное сообщение библиотеки
    /// "Failed to load the image" наружу не выходит никогда.
    /// Если контракт будут менять — этот тест должен покраснеть.
    /// </summary>
    [Fact]
    public void BadImageFileThrowsArgumentException()
    {
        var comparer = Comparer();
        var missing = Path.Combine(AppContext.BaseDirectory, "tests", "no-such-image.jpg");
        var broken = Path.Combine(Path.GetTempPath(), $"not-an-image-{Guid.NewGuid():N}.jpg");
        File.WriteAllText(broken, "это не картинка");

        try
        {
            Assert.Throws<ArgumentException>(() => comparer.CompareFiles(missing, Cmp("1")));
            Assert.Throws<ArgumentException>(() => comparer.CompareFiles(Orig("1"), missing));
            Assert.Throws<ArgumentException>(() => comparer.CompareFiles(broken, Cmp("1")));
            Assert.Throws<ArgumentException>(() => comparer.CompareFiles(Orig("1"), broken));
        }
        finally
        {
            File.Delete(broken);
        }
    }

    [Fact]
    public void CompareMatsRejectsNullAndEmpty()
    {
        var comparer = Comparer();
        using var mat = new Mat(10, 10, DepthType.Cv8U, 1);
        using var empty = new Mat();

        Assert.Throws<ArgumentException>(() => comparer.CompareMats(null!, mat));
        Assert.Throws<ArgumentException>(() => comparer.CompareMats(mat, null!));
        Assert.Throws<ArgumentException>(() => comparer.CompareMats(empty, mat));
        Assert.Throws<ArgumentException>(() => comparer.CompareMats(mat, empty));
    }
}
