using Reloaded.Mod.Interfaces;
using TimeStranger.AnimationFix.Configuration;

namespace TimeStranger.AnimationFix.Template.Configuration;

public class ConfiguratorMixinBase
{
    public virtual IUpdatableConfigurable[] MakeConfigurations(string configFolder) =>
    [
        Configurable<Config>.FromFile(Path.Combine(configFolder, "Config.json"), "Default Config"),
    ];

    public virtual bool TryRunCustomConfiguration(Configurator configurator) => false;

    public virtual void Migrate(string oldDirectory, string newDirectory)
    {
    }
}
