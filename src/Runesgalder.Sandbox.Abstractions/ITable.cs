namespace Runesgalder.Sandbox;

/// <summary>A read-only table supplied by the host.</summary>
public interface ITable
{
    /// <summary>The table name.</summary>
    string Name { get; }

    /// <summary>The column names.</summary>
    IReadOnlyList<string> Columns { get; }

    /// <summary>The string-formatted rows.</summary>
    IReadOnlyList<IReadOnlyList<string?>> Rows { get; }
}
