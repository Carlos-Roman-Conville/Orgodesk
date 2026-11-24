using System.Xml.Linq;

namespace Orgodesk.Commands.Tests;

/// <summary>FC-64: build settings that keep the first start of the published exe fast must not be dropped by accident.</summary>
public class BuildSettingsTests
{
    private static string AppProject([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "Orgodesk.App", "Orgodesk.App.csproj"));

    [Fact]
    public void The_single_file_exe_is_published_compressed_so_its_first_start_is_not_slowed_by_a_103_MB_file()
    {
        var props = XDocument.Load(AppProject()).Descendants("PropertyGroup").SelectMany(g => g.Elements()).ToList();
        Assert.Equal("true", props.Single(e => e.Name == "EnableCompressionInSingleFile").Value);
    }

    [Fact]
    public void Command_mode_is_the_first_thing_Main_does_so_nothing_slow_runs_before_it()
    {
        string main = File.ReadAllText(Path.Combine(Path.GetDirectoryName(AppProject())!, "Program.cs"));
        int at = main.IndexOf("private static int Main(", StringComparison.Ordinal);
        string body = main[at..];
        Assert.True(body.IndexOf("Stdio.Wanted(args)", StringComparison.Ordinal) < body.IndexOf("MigrateLegacyRoot", StringComparison.Ordinal));
        Assert.True(body.IndexOf("Stdio.Wanted(args)", StringComparison.Ordinal) < body.IndexOf("ApplicationConfiguration.Initialize", StringComparison.Ordinal));
    }
}
