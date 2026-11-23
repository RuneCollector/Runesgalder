using Runesgalder.Sandbox;

namespace Runesgalder.SandboxTests;

public sealed class SandboxPolicyTests
{
    [Fact]
    public void DefaultPolicyDeniesNetworkAndIsDeterministic()
    {
        var policy = SandboxPolicy.Default;

        Assert.False(policy.NetworkEnabled);
        Assert.Empty(policy.ReadOnlyFiles);
        Assert.True(policy.Deterministic);
        Assert.Equal(256L * 1024 * 1024, policy.MaxMemoryBytes);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.WallClockTimeout);
    }

    [Fact]
    public void BuilderRejectsRootedAndTraversalPaths()
    {
        var builder = SandboxPolicy.CreateBuilder();

        Assert.Throws<ArgumentException>(() => { _ = builder.AddReadOnlyFile("/etc/passwd"); });
        Assert.Throws<ArgumentException>(() => { _ = builder.AddReadOnlyFile("../secret.csv"); });
        Assert.Throws<ArgumentException>(() => { _ = builder.AddReadOnlyFile("data/../../secret.csv"); });
    }

    [Fact]
    public void BuilderAcceptsRelativePathsAndBuildsAnImmutableSnapshot()
    {
        var builder = SandboxPolicy.CreateBuilder().AddReadOnlyFile("data/input.csv");

        var policy = builder.Build();
        builder.AddReadOnlyFile("data/other.csv");

        Assert.Single(policy.ReadOnlyFiles);
        Assert.Equal("data/input.csv", policy.ReadOnlyFiles[0]);
    }
}
