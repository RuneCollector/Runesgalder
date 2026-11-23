using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Runesgalder.Mcp;

internal static class ToolCatalog
{
    private static readonly IReadOnlyList<Tool> All =
    [
        new Tool
        {
            Name = "compile_csharp",
            Title = "Compile a C# analysis script",
            Description =
                "Compiles C# that implements Runesgalder.Sandbox.IAnalysis for the analysis/v1 contract. " +
                "Compilation runs on the host and never executes the script.",
            InputSchema = Schema(
                """
                {
                  "type": "object",
                  "properties": {
                    "code": {
                      "type": "string",
                      "maxLength": 262144,
                      "description": "The C# source that implements the analysis contract."
                    },
                    "contract": {
                      "type": "string",
                      "enum": ["analysis"],
                      "description": "The execution contract. Only 'analysis' is supported in v0.1."
                    }
                  },
                  "required": ["code"],
                  "additionalProperties": false
                }
                """),
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = true,
                IdempotentHint = true,
                OpenWorldHint = false,
                DestructiveHint = false
            }
        },
        new Tool
        {
            Name = "run_csharp",
            Title = "Run a C# analysis in the sandbox",
            Description =
                "Compiles and runs C# analysis code in the hardened worker container. " +
                "Input data is inline CSV only; file paths, URLs, and environment values are not accepted. " +
                "Returns a run envelope; use get_run with the run_id to poll.",
            InputSchema = Schema(
                """
                {
                  "type": "object",
                  "properties": {
                    "code": {
                      "type": "string",
                      "maxLength": 262144,
                      "description": "The C# source that implements the analysis contract."
                    },
                    "data": {
                      "type": "object",
                      "properties": {
                        "name": {
                          "type": "string",
                          "maxLength": 128,
                          "description": "A label for the table. It is not a path."
                        },
                        "csv": {
                          "type": "string",
                          "maxLength": 8388608,
                          "description": "The CSV content, with a header row. Omit data for an empty table."
                        }
                      },
                      "required": ["csv"],
                      "additionalProperties": false
                    },
                    "wait_seconds": {
                      "type": "integer",
                      "minimum": 0,
                      "maximum": 120,
                      "default": 30,
                      "description": "How long to wait for a terminal status. Zero returns immediately after queueing."
                    },
                    "wall_clock_seconds": {
                      "type": "number",
                      "exclusiveMinimum": 0,
                      "maximum": 120,
                      "description": "The analysis wall-clock limit. It may only lower the server maximum."
                    }
                  },
                  "required": ["code"],
                  "additionalProperties": false
                }
                """),
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = false,
                IdempotentHint = false,
                OpenWorldHint = false,
                DestructiveHint = false
            }
        },
        new Tool
        {
            Name = "get_run",
            Title = "Get a sandbox run",
            Description =
                "Returns the current envelope for a run. Terminal runs are retained for 30 minutes, up to 100 runs.",
            InputSchema = Schema(
                """
                {
                  "type": "object",
                  "properties": {
                    "run_id": {
                      "type": "string",
                      "maxLength": 128,
                      "description": "The opaque run identifier returned by run_csharp."
                    }
                  },
                  "required": ["run_id"],
                  "additionalProperties": false
                }
                """),
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = true,
                IdempotentHint = true,
                OpenWorldHint = false,
                DestructiveHint = false
            }
        },
        new Tool
        {
            Name = "cancel_run",
            Title = "Cancel a sandbox run",
            Description =
                "Requests cooperative cancellation of a queued or running run, then terminates the container at the hard deadline. " +
                "Cancelling a terminal run returns its terminal status unchanged.",
            InputSchema = Schema(
                """
                {
                  "type": "object",
                  "properties": {
                    "run_id": {
                      "type": "string",
                      "maxLength": 128,
                      "description": "The opaque run identifier returned by run_csharp."
                    }
                  },
                  "required": ["run_id"],
                  "additionalProperties": false
                }
                """),
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = false,
                IdempotentHint = true,
                OpenWorldHint = false,
                DestructiveHint = false
            }
        }
    ];

    public static IList<Tool> Create() => [.. All];

    private static JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
