using System.Reflection;
using Dragonhill.CMakeFileWatcher.Cli.Config;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Dragonhill.CMakeFileWatcher.Cli;

public static class Program
{
    public static async Task<int> Main()
    {
        var versionString = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        
        Console.WriteLine($"Dragonhill.CMakeFileWatcher v{versionString}");
        
        try
        {
            WatcherConfig? config;

            if (!File.Exists(Constants.ConfigFileName))
            {
                throw new ConfigurationException($"Config file '{Constants.ConfigFileName}' does not exist in '{Directory.GetCurrentDirectory()}'");
            }

            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                using var configFileReader = File.OpenText(Constants.ConfigFileName);

                config = deserializer.Deserialize<WatcherConfig>(configFileReader);
            }
            catch (Exception exception)
            {
                throw new ConfigurationException($"There has been an error reading the config file: {exception.Message}");
            }

            // Note config might be null so the code analysis message is wrong here
            if (config?.Roots == null || config.Roots.Count == 0)
            {
                throw new ConfigurationException("Config file does not contain any roots");
            }

            CancellationTokenSource source = new ();
            var token = source.Token;
            
            var watcherTasks = config.Roots.Select(configEntry => RootWatcher.Run(Directory.GetCurrentDirectory(), configEntry, token)).ToArray();

            Console.CancelKeyPress += (_, _) =>
            {
                source.Cancel();
            };

            Console.WriteLine("Please press <Ctrl-C> to quit.");

            await Task.WhenAll(watcherTasks);
        }
        catch (ConfigurationException exception)
        {
            await Console.Error.WriteLineAsync("There has been a configuration error:\n" + exception.Message);
            return 1;
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync("There has been an internal error:\n" + exception.Message);
            return 1;
        }

        return 0;
    }
}
