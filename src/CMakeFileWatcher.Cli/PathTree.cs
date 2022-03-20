using System.Text;

namespace Dragonhill.CMakeFileWatcher.Cli;

internal class PathTree
{
    public enum NodeType
    {
        None,
        Inner,
        Leaf
    }
    
    private class Node
    {
        public Node? Parent { get; set; }
        public string? Name { get; set; }
        public PatternGroup? Payload { get; init; }
        public Dictionary<string, Node>? Children { get; set; } 
    }

    private readonly Node _rootNode = new()
    {
        Children = new Dictionary<string, Node>()
    };

    public bool Changed { get; private set; }

    public string GenerateCMakeContent(IReadOnlyList<PatternGroup> patternGroups)
    {
        Changed = false;

        foreach (var patternGroup in patternGroups)
        {
            patternGroup.ResetContent();
        }
        
        foreach (var childNode in _rootNode.Children!.Values)
        {
            WalkTree(string.Empty, childNode);
        }

        StringBuilder builder = new();
        
        foreach (var patternGroup in patternGroups)
        {
            patternGroup.FinishContent(builder);
        }

        return builder.ToString();
    }

    private static void WalkTree(string parentPath, Node node)
    {
        if (node.Payload != null)
        {
            node.Payload.AddContent(parentPath + node.Name);
            return;
        }
        
        foreach (var childNode in node.Children!.Values)
        {
            WalkTree(parentPath + node.Name + '/', childNode);
        }
    }

    public NodeType GetNodeType(Span<string> pathParts)
    {
        var node = GetNode(pathParts);

        if (node == null)
        {
            return NodeType.None;
        }

        return node.Payload != null ? NodeType.Leaf : NodeType.Inner;
    }

    public void AddFile(Span<string> pathParts, PatternGroup payload)
    {
        var parentNode = AddDirectoryPath(pathParts[..^1]);
        
        if (parentNode.Payload != null)
        {
            throw new InvalidOperationException($"Cannot add '{pathParts.GetCombinedPathParts()}' as child of a file '{parentNode.Name}'");
        }

        parentNode.Children ??= new Dictionary<string, Node>();

        var name = pathParts[^1];

        if (parentNode.Children.TryGetValue(name, out var leafNode))
        {
            if (!ReferenceEquals(leafNode.Payload, payload))
            {
                throw new InvalidOperationException($"Cannot add '{pathParts.GetCombinedPathParts()}' with a different payload than existing entry");
            }
        }
        else
        {
            parentNode.Children.Add(name, new Node
            {
                Parent = parentNode,
                Name = pathParts[^1],
                Payload = payload
            });

            Changed = true;
        }
    }

    public void RenamePath(Span<string> oldPathParts, Span<string> newPathParts)
    {
        var node = TakeNodeOutOfTree(oldPathParts);

        if (node == null)
        {
            // Old path not within tree, ignore
            return;
        }

        node.Name = newPathParts[^1];

        var parentNode = AddDirectoryPath(newPathParts[..^1]);

        if (parentNode.Payload != null)
        {
            throw new InvalidOperationException($"Cannot move '{oldPathParts.GetCombinedPathParts()}' to '{newPathParts.GetCombinedPathParts()}' as child of a file '{parentNode.Name}'");
        }

        node.Parent = parentNode;

        parentNode.Children ??= new Dictionary<string, Node>();

        if (!parentNode.Children.TryAdd(node.Name, node))
        {
            throw new InvalidOperationException($"Cannot move '{oldPathParts.GetCombinedPathParts()}' to '{newPathParts.GetCombinedPathParts()}' because target path exists in tree");
        }

        Changed = true;
    }

    public void RemovePath(Span<string> pathParts)
    {
        if (TakeNodeOutOfTree(pathParts) != null)
        {
            Changed = true;
        }
    }

    private Node? GetNode(Span<string> pathParts)
    {
        var currentNode = _rootNode;

        foreach (var pathPart in pathParts)
        {
            if (currentNode.Children == null)
            {
                return null;
            }

            if (!currentNode.Children.TryGetValue(pathPart, out currentNode))
            {
                return null;
            }
        }

        return currentNode;
    }

    private Node? TakeNodeOutOfTree(Span<string> pathParts)
    {
        var resultNode = GetNode(pathParts);

        if (resultNode == null)
        {
            return null;
        }
        
        var currentNode = resultNode;

        do
        {
            var parent = currentNode.Parent!;

            if (parent.Children!.Count > 1)
            {
                parent.Children.Remove(currentNode.Name!);
                break;
            }

            // If the directory node would become a leaf node, remove it too and recurse
            currentNode = parent;
        } while (currentNode.Parent != null);

        resultNode.Parent = null;

        return resultNode;
    }

    private Node AddDirectoryPath(Span<string> path)
    {
        var currentNode = _rootNode;
        
        foreach (var pathPart in path)
        {
            if (currentNode.Payload != null)
            {
                throw new InvalidOperationException($"Cannot add '{path.GetCombinedPathParts()}' as child of a file '{currentNode.Name}'");
            }

            currentNode.Children ??= new Dictionary<string, Node>();
            
            if (!currentNode.Children.TryGetValue(pathPart, out var nextNode))
            {
                nextNode = new Node
                {
                    Parent = currentNode,
                    Name = pathPart
                };
                
                currentNode.Children.Add(pathPart, nextNode);
            }

            currentNode = nextNode;
        }

        return currentNode;
    }
}
