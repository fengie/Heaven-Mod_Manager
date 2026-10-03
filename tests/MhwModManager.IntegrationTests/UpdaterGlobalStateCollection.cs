using Xunit;

namespace MhwModManager.IntegrationTests;

[CollectionDefinition("Updater global state", DisableParallelization = true)]
public sealed class UpdaterGlobalStateFixture
{
    public const string Name = "Updater global state";
}
