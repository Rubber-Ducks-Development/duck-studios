using System.IO;

namespace DuckStudio;

public static class ProjectFiles
{
    public const string Extension = ".duckproj";
    public const string DurtyExtension = ".dctproj";
    public const string SaveFilter = "Duck Studio project (*.duckproj)|*.duckproj";
    public const string OpenFilter = "Clothing projects (*.duckproj;*.duck;*.weftproj;*.dctproj)|*.duckproj;*.duck;*.weftproj;*.dctproj|Duck Studio projects (*.duckproj)|*.duckproj|Legacy Duck Studio projects (*.duck;*.weftproj)|*.duck;*.weftproj|Durty Cloth projects (*.dctproj)|*.dctproj";
    public static IEnumerable<string> NativeExtensions => [Extension, ".duck", ".weftproj"];
    public static IEnumerable<string> OpenWithExtensions => [.. NativeExtensions, DurtyExtension];
    public static bool IsCurrentProject(string path) => Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase);
    public static bool IsNativeProject(string path) => NativeExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public static bool IsDurtyProject(string path) => Path.GetExtension(path).Equals(DurtyExtension, StringComparison.OrdinalIgnoreCase);
    public static string SuggestedSavePath(string path) => Path.ChangeExtension(path, Extension);
    public static IEnumerable<string> EnumerateNativeProjects(string folder) => Directory.EnumerateFiles(folder).Where(IsNativeProject);
}
