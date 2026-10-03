using Xunit;

namespace MhwModManager.IntegrationTests;

[CollectionDefinition("Updater global state", DisableParallelization = true)]
public sealed class UpdaterGlobalStateCollection
{
    public const string Name = "Updater global state";
}
