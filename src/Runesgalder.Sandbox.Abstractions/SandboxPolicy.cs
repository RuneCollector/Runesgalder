namespace Runesgalder.Sandbox;

/// <summary>Immutable policy applied to a sandbox execution.</summary>
public sealed class SandboxPolicy
{
    private SandboxPolicy(Builder builder)
    {
        NetworkEnabled = builder.NetworkEnabled;
        ReadOnlyFiles = Array.AsReadOnly(builder.ReadOnlyFiles.ToArray());
        MaxMemoryBytes = builder.MaxMemoryBytes;
        WallClockTimeout = builder.WallClockTimeout;
        MaxCpuCores = builder.MaxCpuCores;
        MaxProcesses = builder.MaxProcesses;
        MaxOutputBytes = builder.MaxOutputBytes;
        Deterministic = builder.Deterministic;
    }

    /// <summary>The default deny-by-default policy.</summary>
    public static SandboxPolicy Default { get; } = new Builder().Build();

    /// <summary>Whether outbound network access is enabled.</summary>
    public bool NetworkEnabled { get; }

    /// <summary>Relative virtual paths exposed read-only to generated code.</summary>
    public IReadOnlyList<string> ReadOnlyFiles { get; }

    /// <summary>The memory limit in bytes.</summary>
    public long MaxMemoryBytes { get; }

    /// <summary>The wall-clock limit.</summary>
    public TimeSpan WallClockTimeout { get; }

    /// <summary>The CPU core limit.</summary>
    public int MaxCpuCores { get; }

    /// <summary>The process and thread limit.</summary>
    public int MaxProcesses { get; }

    /// <summary>The maximum captured output in bytes.</summary>
    public long MaxOutputBytes { get; }

    /// <summary>Whether the deterministic clock and random source are enabled.</summary>
    public bool Deterministic { get; }

    /// <summary>Creates a policy builder.</summary>
    /// <returns>A new builder.</returns>
    public static Builder CreateBuilder() => new();

    /// <summary>Builds sandbox policies.</summary>
    public sealed class Builder
    {
        private readonly List<string> _readOnlyFiles = [];

        /// <summary>Gets or sets whether outbound network access is enabled.</summary>
        public bool NetworkEnabled { get; set; }

        /// <summary>Gets the read-only virtual file paths.</summary>
        public IReadOnlyList<string> ReadOnlyFiles => _readOnlyFiles;

        /// <summary>Gets or sets the memory limit in bytes.</summary>
        public long MaxMemoryBytes { get; set; } = 256L * 1024 * 1024;

        /// <summary>Gets or sets the wall-clock limit.</summary>
        public TimeSpan WallClockTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Gets or sets the CPU core limit.</summary>
        public int MaxCpuCores { get; set; } = 1;

        /// <summary>Gets or sets the process and thread limit.</summary>
        public int MaxProcesses { get; set; } = 16;

        /// <summary>Gets or sets the maximum captured output in bytes.</summary>
        public long MaxOutputBytes { get; set; } = 1024 * 1024;

        /// <summary>Gets or sets whether the deterministic clock and random source are enabled.</summary>
        public bool Deterministic { get; set; } = true;

        /// <summary>Adds a read-only virtual file path.</summary>
        /// <param name="virtualPath">A relative path without traversal segments.</param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentException">Thrown when the path is rooted, contains traversal, or is empty.</exception>
        public Builder AddReadOnlyFile(string virtualPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

            if (Path.IsPathRooted(virtualPath))
            {
                throw new ArgumentException("Read-only paths must be relative.", nameof(virtualPath));
            }

            var segments = virtualPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment == ".."))
            {
                throw new ArgumentException("Read-only paths must not contain parent traversal.", nameof(virtualPath));
            }

            _readOnlyFiles.Add(virtualPath);
            return this;
        }

        /// <summary>Builds the policy.</summary>
        /// <returns>An immutable policy.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a limit is not positive.</exception>
        public SandboxPolicy Build()
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaxMemoryBytes, 0);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaxCpuCores, 0);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaxProcesses, 0);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaxOutputBytes, 0);

            if (WallClockTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(WallClockTimeout), "The wall-clock limit must be positive.");
            }

            return new SandboxPolicy(this);
        }
    }
}
