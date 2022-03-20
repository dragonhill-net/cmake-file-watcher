namespace Dragonhill.CMakeFileWatcher.Cli.Config;

public class WatcherRootConfig
{
    public string? Path { get; set; }
    public string? GeneratedFilePath { get; set; }
    public IList<WatcherPatternGroup>? PatternGroups { get; set; }
}
