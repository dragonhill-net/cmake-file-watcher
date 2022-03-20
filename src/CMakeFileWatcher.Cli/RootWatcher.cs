using Dragonhill.CMakeFileWatcher.Cli.Config;

namespace Dragonhill.CMakeFileWatcher.Cli
{
    internal class RootWatcher
    {
        private const int RecoverySeconds = 2;

        private readonly object _lock = new();

        private readonly SemaphoreSlim _errorSemaphore = new(0, 1);
        private Exception? _lastError;
        
        private readonly WatcherRootConfig _config;
        private FileSystemWatcher? _watcher;
        private readonly string _rootPath;
        private readonly string[] _rootPathParts;
        private readonly string _outputPath;

        private readonly PathTree _tree = new();
        private readonly Dictionary<string, PatternGroup> _extensionMapping = new();
        private readonly List<PatternGroup> _patternGroups = new();

        public static Task Run(string basePath, WatcherRootConfig config, CancellationToken cancellationToken)
        {
            RootWatcher instance = new(basePath, config);
            return instance.Run(cancellationToken);
        }
        
        private RootWatcher(string basePath, WatcherRootConfig config)
        {
            _config = config;
            
            var basePathParts = basePath.GetPathParts();

            if (config.Path == null)
            {
                throw new ConfigurationException("A root config must contain a relative path to watch");
            }

            _rootPath = Path.GetFullPath(Path.Combine(basePath, config.Path));

            _rootPathParts = _rootPath.GetPathParts();

            if (!basePathParts.IsPathParentOf(_rootPathParts))
            {
                throw new ConfigurationException($"The path '{config.Path}' is not a valid relative path");
            }
            
            BuildPatternGroups();

            _outputPath = CheckOutputPath(basePath, basePathParts);
        }

        private void BuildPatternGroups()
        {
            if (_config.PatternGroups == null || _config.PatternGroups.Count == 0)
            {
                throw new ConfigurationException($"Root config for '{_rootPath}' has no pattern groups");
            }
            
            foreach (var group in _config.PatternGroups)
            {
                if (group.ListName == null)
                {
                    throw new ConfigurationException($"Root config for '{_rootPath}' contains a pattern group without a listName");
                }

                if (group.Extensions == null || group.Extensions.Count == 0)
                {
                    throw new ConfigurationException($"Root config for '{_rootPath}' contains a pattern group without extensions");
                }
                
                var patternGroup = new PatternGroup(group.ListName);

                foreach(var extension in group.Extensions)
                {
                    if (!_extensionMapping.TryAdd(extension, patternGroup))
                    {
                        throw new InvalidOperationException($"Multiple use of extension {extension} within the root {_config.Path}");
                    }
                }
                
                _patternGroups.Add(patternGroup);
            }
        }

        private string CheckOutputPath(string basePath, IReadOnlyList<string> basePathParts)
        {
            if (_config.GeneratedFilePath == null)
            {
                throw new ConfigurationException("A root config must contain a relative path to a file to be generated");
            }
            
            var outputPath = Path.GetFullPath(Path.Combine(basePath, _config.GeneratedFilePath));
            
            var outputPathParts = outputPath.GetPathParts();

            if (!basePathParts.IsPathParentOf(outputPathParts))
            {
                throw new ConfigurationException($"Generated file path '{outputPath}' does not start with the base path");
            }

            if (basePathParts.Count == outputPathParts.Length)
            {
                throw new ConfigurationException($"Generated file path '{outputPath}' cannot be the base path");
            }

            var outputPathExtension = Path.GetExtension(outputPath);

            if (_extensionMapping.ContainsKey(outputPathExtension))
            {
                throw new ConfigurationException($"Generated file path under the root {_rootPath} cannot use a watched extension");
            }

            return outputPath;
        }

        private async Task Run(CancellationToken cancellationToken)
        {
            for (;;)
            {
                // Acquire the lock so that possible events are queued up till the scan is finished
                try
                {
                    lock (_lock)
                    {
                        _watcher = InitFileWatcher();

                        DoFullScan();

                        UpdateTargetFile(true);
                    }
                }
                catch (Exception exception)
                {
                    QueueError(exception);
                }

                try
                {
                    await _errorSemaphore.WaitAsync(cancellationToken);

                    lock (_lock)
                    {
                        if (_lastError == null)
                        {
                            throw new InvalidCastException("Error semaphore triggered and error not set!");
                        }

                        Console.Error.WriteLine($"There has been an error (Trying to recover in {RecoverySeconds} seconds):\n{_lastError.Message}");

                        _lastError = null;

                        if (_errorSemaphore.CurrentCount != 0)
                        {
                            throw new InvalidCastException("Semaphore is in an unexpected state");
                        }
                    }

                    await Task.Delay(TimeSpan.FromSeconds(RecoverySeconds), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException exception)
                {
                    throw new InvalidOperationException("Internal error: object disposed!", exception);
                }
            }

            lock (_lock)
            {
                _watcher?.Dispose();
                _errorSemaphore.Dispose();
            }
        }

        private void QueueError(Exception exception)
        {
            if (_watcher != null)
            {
                _watcher.Dispose();
                _watcher = null;
            }

            if (_lastError != null)
            {
                Console.Error.WriteLine($"Internal Error: Discarding additional error: {exception.Message}");
                return;
            }

            _lastError = exception;
            if (_errorSemaphore.CurrentCount == 0)
            {
                _errorSemaphore.Release();
            }
        }
        
        private void WrapEventAction(object sender, Action action)
        {
            lock (_lock)
            {
                if (!ReferenceEquals(_watcher, sender))
                {
                    return;
                }
                
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    QueueError(exception);
                }
            }
        }

        private void UpdateTargetFile(bool initial = false)
        {
            if (!initial && !_tree.Changed)
            {
                return;
            }
            
            var cMakeContent = _tree.GenerateCMakeContent(_patternGroups);

            if (initial )
            {
                try
                {
                    var existingContent = File.ReadAllText(_outputPath);

                    if (existingContent == cMakeContent)
                    {
                        return;
                    }
                }
                catch
                {
                    // Any errors reading the existing target are ignored
                }
            }

            try
            {
                File.WriteAllText(_outputPath, cMakeContent);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"There has been an error writing the target file '{_outputPath}': '{exception.Message}'", exception);
            }
        }

        private FileSystemWatcher InitFileWatcher()
        {
            if (!Directory.Exists(_rootPath))
            {
                throw new InvalidOperationException($"Path to watch '{_rootPath}' is not a directory");
            }
            
            var watcher = new FileSystemWatcher(_rootPath);

            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName;
            
            watcher.Created += OnCreated;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;

            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;

            return watcher;
        }

        private void DoFullScan()
        {
            WalkFilesystem(new DirectoryInfo(_rootPath));
        }

        private void WalkFilesystem(DirectoryInfo currentDirectory)
        {
            foreach (var file in currentDirectory.EnumerateFiles())
            {
                CheckAddFilePath(file.FullName);
            }

            foreach (var directory in currentDirectory.EnumerateDirectories())
            {
                WalkFilesystem(directory);
            }
        }

        private void CheckAddFilePath(string path)
        {
            var extension = path.GetExtensionOnly();
            
            if (!_extensionMapping.TryGetValue(extension, out var patternGroup))
            {
                return;
            }

            // If it is something like a directory with a matching extension, ignore it
            if (!File.Exists(path))
            {
                return;
            }

            var pathParts = path.GetPathParts();
            var relativePath = pathParts.GetRelativePathTo(_rootPathParts);

            _tree.AddFile(relativePath.AsSpan(), patternGroup);
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            WrapEventAction(sender, () =>
            {
                CheckAddFilePath(e.FullPath);
                UpdateTargetFile();
            });
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            WrapEventAction(sender, () =>
            {
                var relativePath = e.FullPath.GetPathParts().GetRelativePathTo(_rootPathParts);
                _tree.RemovePath(relativePath.AsSpan());
                UpdateTargetFile();
            });
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            WrapEventAction(sender, () =>
            {
                var oldRelativePath = e.OldFullPath.GetPathParts().GetRelativePathTo(_rootPathParts).AsSpan();
                var newRelativePath = e.FullPath.GetPathParts().GetRelativePathTo(_rootPathParts).AsSpan();

                var oldNodeType = _tree.GetNodeType(oldRelativePath);
                
                var newValidExtension = _extensionMapping.ContainsKey(e.FullPath.GetExtensionOnly());

                switch (oldNodeType)
                {
                    case PathTree.NodeType.Leaf when newValidExtension == false: // If it was a valid file but is no longer, remove it
                        _tree.RemovePath(oldRelativePath);
                        break;
                    
                    case PathTree.NodeType.None when newValidExtension: // If it was not in the tree but has a valid new extension try add it
                        CheckAddFilePath(e.FullPath);
                        break;
                    
                    case PathTree.NodeType.Inner: // If it was an inner node (directory), it should still be - just rename the node
                    case PathTree.NodeType.Leaf: // If it was a valid file and still is - just rename the node
                        _tree.RenamePath(oldRelativePath, newRelativePath);
                        break;
                }
                
                UpdateTargetFile();
            });
        }
        
        private void OnError(object sender, ErrorEventArgs e)
        {
            WrapEventAction(sender, () => QueueError(e.GetException()));
        }
    }
}
