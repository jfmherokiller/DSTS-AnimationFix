using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Reloaded.Mod.Interfaces;

namespace TimeStranger.AnimationFix.Template.Configuration;

public class Configurable<TParentType> : IUpdatableConfigurable
    where TParentType : Configurable<TParentType>, new()
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    [Browsable(false)]
    public event Action<IUpdatableConfigurable>? ConfigurationUpdated;

    [JsonIgnore]
    [Browsable(false)]
    public string? FilePath { get; private set; }

    [JsonIgnore]
    [Browsable(false)]
    public string? ConfigName { get; private set; }

    [JsonIgnore]
    [Browsable(false)]
    private FileSystemWatcher? ConfigWatcher { get; set; }

    [JsonIgnore]
    [Browsable(false)]
    public Action? Save { get; private set; }

    [Browsable(false)]
    private static readonly object ReadLock = new();

    public static TParentType FromFile(string filePath, string configName) => ReadFrom(filePath, configName);

    private void Initialize(string filePath, string configName)
    {
        FilePath = filePath;
        ConfigName = configName;
        ConfigWatcher = new FileSystemWatcher(Path.GetDirectoryName(filePath)!, Path.GetFileName(filePath))
        {
            EnableRaisingEvents = true,
        };
        ConfigWatcher.Changed += (_, _) => OnConfigurationUpdated();
        Save = OnSave;
    }

    public void DisposeEvents()
    {
        ConfigWatcher?.Dispose();
        ConfigurationUpdated = null;
    }

    private void OnConfigurationUpdated()
    {
        lock (ReadLock)
        {
            var newConfig = Utilities.TryGetValue(() => ReadFrom(FilePath!, ConfigName!), 250, 2);
            newConfig.ConfigurationUpdated = ConfigurationUpdated;
            DisposeEvents();
            newConfig.ConfigurationUpdated?.Invoke(newConfig);
        }
    }

    private void OnSave() =>
        File.WriteAllText(FilePath!, JsonSerializer.Serialize((TParentType)this, SerializerOptions));

    private static TParentType ReadFrom(string filePath, string configName)
    {
        var result = (File.Exists(filePath)
            ? JsonSerializer.Deserialize<TParentType>(File.ReadAllBytes(filePath), SerializerOptions)
            : new TParentType()) ?? new TParentType();
        result.Initialize(filePath, configName);
        return result;
    }
}
