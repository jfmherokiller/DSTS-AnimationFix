using System.ComponentModel;
using TimeStranger.AnimationFix.Template.Configuration;

namespace TimeStranger.AnimationFix.Configuration;

public class Config : Configurable<Config>
{
    [DisplayName("Enable Advanced Character Overrides")]
    [Description("Allows AnimationFallbacks.json rules marked overrideExisting=true to replace " +
                 "animations that already exist, and enables the wildcard '*' character profile. " +
                 "Leave disabled for the safe missing-animation-only behavior.")]
    [DefaultValue(false)]
    public bool EnableAdvancedCharacterOverrides { get; set; }
}

public class ConfiguratorMixin : ConfiguratorMixinBase
{
}
