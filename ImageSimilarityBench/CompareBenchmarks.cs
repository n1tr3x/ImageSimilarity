using BenchmarkDotNet.Attributes;
using Emgu.CV;
using ImageSimilarity;

namespace ImageSimilarityBench;

[MemoryDiagnoser]
[InProcess]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class CompareBenchmarks
{
    private ImageHomographyComparer _comparer = null!;
    private string _origPath = null!;
    private string _cmpPath = null!;
    private Mat _origMat = null!;
    private Mat _cmpMat = null!;

    /// <summary>
    /// Имена подпапок в tests/. Бенч прогоняется на каждой.
    /// </summary>
    [Params("1", "2", "3", "4")]
    public string TestCase { get; set; } = "1";

    [GlobalSetup]
    public void Setup()
    {
        _comparer = new ImageHomographyComparer();

        var testDir = Path.Combine(AppContext.BaseDirectory, "tests", TestCase);
        _origPath = Path.Combine(testDir, "orig.jpg");
        _cmpPath = Path.Combine(testDir, "cmp.jpg");

        if (!File.Exists(_origPath) || !File.Exists(_cmpPath))
            throw new FileNotFoundException(
                $"Test case '{TestCase}' missing files in {testDir}");

        _origMat = CvInvoke.Imread(_origPath);
        _cmpMat = CvInvoke.Imread(_cmpPath);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _origMat?.Dispose();
        _cmpMat?.Dispose();
    }

    /// <summary>
    /// Холодный путь: Imread + полный пайплайн. Меряет всё что видит пользователь.
    /// </summary>
    [Benchmark(Baseline = true)]
    public double CompareFiles()
    {
        return _comparer.CompareFiles(_origPath, _cmpPath).SimilarityScore;
    }

    /// <summary>
    /// Тёплый путь: Mat'ы уже в памяти. Изолирует стоимость алгоритма
    /// (ORB + matching + RANSAC) от стоимости I/O и JPEG-декода.
    /// </summary>
    [Benchmark]
    public double CompareMats()
    {
        return _comparer.CompareMats(_origMat, _cmpMat).SimilarityScore;
    }
}
