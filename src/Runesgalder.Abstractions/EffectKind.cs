namespace Runesgalder;

/// <summary>Classifies an observable side effect performed by a run.</summary>
public enum EffectKind
{
    /// <summary>A model invocation.</summary>
    ModelCall,

    /// <summary>A tool invocation.</summary>
    ToolCall,

    /// <summary>Execution of generated code inside a sandbox.</summary>
    CodeExecution,

    /// <summary>A human approval or data-entry step.</summary>
    HumanStep
}
