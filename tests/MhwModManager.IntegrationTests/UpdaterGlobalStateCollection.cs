using Xunit;

namespace MhwModManager.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UpdaterGlobalStateCollection
{
    public const string Name = "Updater global state";
}
