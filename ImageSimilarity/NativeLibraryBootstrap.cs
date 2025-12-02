using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ImageSimilarity.Native
{
    internal static class NativeLibraryBootstrap
    {
        private const string LogicalLibraryName = "cvextern";
        private const string WindowsLibFileName = "libcvextern.so";
        private const string LinuxLibFileName = "libcvextern.so";

        private static string? _nativeLibFullPath;
        private static bool _initialized;

        [ModuleInitializer] // вызывается автоматически при загрузке сборки
        public static void Initialize()
        {
            if (_initialized)
                return;

            var assembly = typeof(NativeLibraryBootstrap).Assembly;

            // Регистрируем резолвер для всех DllImport в этой сборке
            NativeLibrary.SetDllImportResolver(assembly, ResolveNativeLibrary);

            // Подготавливаем нативную библиотеку заранее (один раз)
            PrepareNativeLibrary(assembly);

            _initialized = true;
        }

        private static void PrepareNativeLibrary(Assembly assembly)
        {
            string libFileName = GetPlatformLibraryFileName();

            // Ищем embedded-ресурс, заканчивающийся на нужное имя файла
            string? resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(libFileName, StringComparison.Ordinal));

            if (resourceName == null)
            {
                // Можно бросить исключение, а можно тихо выйти — на твое усмотрение
                throw new InvalidOperationException(
                    $"Embedded native library resource '{libFileName}' not found in assembly '{assembly.FullName}'. " +
                    "Проверь, что файл добавлен как EmbeddedResource.");
            }

            // Папка, куда будем складывать нативки
            // Можно использовать имя сборки и версию, чтобы избежать конфликтов
            var baseDirName = $"{assembly.GetName().Name}.Native";
            var version = assembly.GetName().Version?.ToString() ?? "1.0.0.0";

            string tempRoot = Path.Combine(Path.GetTempPath(), baseDirName, version);
            Directory.CreateDirectory(tempRoot);

            string targetPath = Path.Combine(tempRoot, libFileName);

            // Если файла нет – пишем. Если хочешь, можно добавить проверку версии/хеша.
            if (!File.Exists(targetPath))
            {
                using Stream? resourceStream = assembly.GetManifestResourceStream(resourceName);
                if (resourceStream == null)
                {
                    throw new InvalidOperationException(
                        $"Resource stream '{resourceName}' is null for native library '{libFileName}'.");
                }

                using FileStream fileStream = File.Create(targetPath);
                resourceStream.CopyTo(fileStream);
            }

            _nativeLibFullPath = targetPath;
        }

        private static IntPtr ResolveNativeLibrary(
            string libraryName,
            Assembly assembly,
            DllImportSearchPath? searchPath)
        {
            // Нас интересуют только вызовы к "cvextern" (логическое имя в DllImport)
            if (!string.Equals(libraryName, LogicalLibraryName, StringComparison.Ordinal) &&
                !string.Equals(libraryName, WindowsLibFileName, StringComparison.Ordinal) &&
                !string.Equals(libraryName, LinuxLibFileName, StringComparison.Ordinal))
            {
                return IntPtr.Zero; // пусть .NET ищет всё остальное сам
            }

            if (_nativeLibFullPath == null)
            {
                // На всякий случай попытаться подготовить ещё раз
                PrepareNativeLibrary(assembly);
            }

            if (_nativeLibFullPath == null)
                throw new InvalidOperationException("Native library path is not initialized.");

            // Если уже загружали — NativeLibrary.Load вернет тот же handle
            return NativeLibrary.Load(_nativeLibFullPath);
        }

        private static string GetPlatformLibraryFileName()
        {
            if (OperatingSystem.IsWindows())
                return WindowsLibFileName;

            if (OperatingSystem.IsLinux())
                return LinuxLibFileName;

            throw new PlatformNotSupportedException(
                $"Unsupported platform: {RuntimeInformation.OSDescription}");
        }
    }
}
