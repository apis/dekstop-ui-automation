using System.ComponentModel;
using System.Text;

namespace BddScenarioAgent.Tools;

public static class FileSystemTools
{
    [Description("Reads the entire content of a file from disk and returns it as a string")]
    public static string ReadFile(
        [Description("Absolute or relative path to the file")] string path)
    {
        if (!File.Exists(path))
            return $"Error: file not found at '{path}'";
        return File.ReadAllText(path);
    }

    [Description("Writes content to a file, creating parent directories if needed")]
    public static string WriteFile(
        [Description("Absolute or relative path to the file")] string path,
        [Description("Content to write")] string content)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
        return $"Wrote {content.Length} characters to '{path}'";
    }

    [Description("Lists files and subdirectories at the given path")]
    public static string ListDir(
        [Description("Directory path to list")] string path)
    {
        if (!Directory.Exists(path))
            return $"Error: directory not found at '{path}'";

        var sb = new StringBuilder();
        foreach (var d in Directory.GetDirectories(path))
            sb.AppendLine($"d  {Path.GetFileName(d)}/");
        foreach (var f in Directory.GetFiles(path))
            sb.AppendLine($"f  {Path.GetFileName(f)} ({new FileInfo(f).Length} bytes)");
        return sb.Length > 0 ? sb.ToString() : "(empty)";
    }
}
