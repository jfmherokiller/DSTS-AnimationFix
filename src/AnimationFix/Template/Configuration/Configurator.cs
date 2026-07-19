using Reloaded.Mod.Interfaces;
using TimeStranger.AnimationFix.Configuration;

namespace TimeStranger.AnimationFix.Template.Configuration;

public class Configurator : IConfiguratorV3
{
    private static readonly ConfiguratorMixin ConfiguratorMixin = new();
    private IUpdatableConfigurable[]? _configurations;

    public string? ModFolder { get; private set; }
    public string? ConfigFolder { get; private set; }
    public ConfiguratorContext Context { get; private set; }
    public IUpdatableConfigurable[] Configurations => _configurations ?? MakeConfigurations();

    public Configurator()
    {
    }

    public Configurator(string configDirectory)
    {
        ConfigFolder = configDirectory;
    }

    private IUpdatableConfigurable[] MakeConfigurations()
    {
        _configurations = ConfiguratorMixin.MakeConfigurations(ConfigFolder!);
        for (var index = 0; index < _configurations.Length; index++)
        {
            var copy = index;
            _configurations[index].ConfigurationUpdated += configurable => _configurations[copy] = configurable;
        }

        return _configurations;
    }

    public TType GetConfiguration<TType>(int index) => (TType)Configurations[index];
    public void Migrate(string oldDirectory, string newDirectory) => ConfiguratorMixin.Migrate(oldDirectory, newDirectory);
    public void SetConfigDirectory(string configDirectory) => ConfigFolder = configDirectory;
    public void SetContext(in ConfiguratorContext context) => Context = context;
    public IConfigurable[] GetConfigurations() => Configurations;
    public bool TryRunCustomConfiguration() => ConfiguratorMixin.TryRunCustomConfiguration(this);
    public void SetModDirectory(string modDirectory) => ModFolder = modDirectory;
}
