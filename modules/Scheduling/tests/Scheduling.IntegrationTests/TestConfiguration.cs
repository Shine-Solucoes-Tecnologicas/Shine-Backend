using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Shine.IntegrationTests;

[CollectionDefinition("database")]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
