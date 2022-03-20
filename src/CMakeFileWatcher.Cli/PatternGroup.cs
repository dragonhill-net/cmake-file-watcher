using System.Text;

namespace Dragonhill.CMakeFileWatcher.Cli;

internal class PatternGroup
{
    private readonly string _listName;
    private StringBuilder? _contentBuilder;

    public PatternGroup(string listName)
    {
        _listName = listName;
    }

    public void ResetContent()
    {
        _contentBuilder = new StringBuilder($"list(APPEND {_listName}\n");
    }

    public void AddContent(string path)
    {
        _contentBuilder!.Append(' ', 4);
        _contentBuilder.Append('"');
        _contentBuilder.Append(path);
        _contentBuilder.Append('"');
        _contentBuilder.Append('\n');
    }

    public void FinishContent(StringBuilder builder)
    {
        _contentBuilder!.Append(")\n\n");

        builder.Append(_contentBuilder);
    }
}
