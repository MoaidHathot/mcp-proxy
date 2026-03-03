using System.Diagnostics;
using McpProxy.Abstractions;
using McpProxy.Core.Debugging;
using ModelContextProtocol.Protocol;

namespace McpProxy.Core.Hooks.BuiltIn;

/// <summary>
/// Configuration for the dump hook.
/// </summary>
public sealed class DumpHookConfiguration
{
    /// <summary>
    /// Gets or sets server names to include in dumps.
    /// If null or empty, all servers are included.
    /// </summary>
    public string[]? ServerFilter { get; set; }

    /// <summary>
    /// Gets or sets tool names to include in dumps.
    /// If null or empty, all tools are included.
    /// </summary>
    public string[]? ToolFilter { get; set; }
}

/// <summary>
/// A pre-invoke and post-invoke hook that dumps request and response payloads for debugging.
/// </summary>
/// <remarks>
/// This hook runs very early in the pre-invoke phase and very late in the post-invoke phase
/// to capture the request before any transformation and the response after all transformations.
/// </remarks>
public sealed class DumpHook : IToolHook
{
    private readonly IRequestDumper _dumper;
    private readonly bool _dumpRequests;
    private readonly bool _dumpResponses;
    private readonly Stopwatch _stopwatch = new();

    /// <summary>
    /// Gets the priority of this hook.
    /// Runs very early for pre-invoke (-999) to capture original request,
    /// and very late for post-invoke (999) to capture final response.
    /// </summary>
    public int Priority => -999;

    /// <summary>
    /// Initializes a new instance of <see cref="DumpHook"/>.
    /// </summary>
    /// <param name="dumper">The request dumper instance.</param>
    /// <param name="dumpRequests">Whether to dump requests.</param>
    /// <param name="dumpResponses">Whether to dump responses.</param>
    public DumpHook(IRequestDumper dumper, bool dumpRequests = true, bool dumpResponses = true)
    {
        _dumper = dumper;
        _dumpRequests = dumpRequests;
        _dumpResponses = dumpResponses;
    }

    /// <summary>
    /// Dumps the request before tool invocation.
    /// </summary>
    public async ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        _stopwatch.Restart();
        if (_dumpRequests)
        {
            await _dumper.DumpRequestAsync(
                context.ServerName,
                context.ToolName,
                context.Request,
                context.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Dumps the response after tool invocation.
    /// </summary>
    public async ValueTask<CallToolResult> OnPostInvokeAsync(HookContext<CallToolRequestParams> context, CallToolResult result)
    {
        _stopwatch.Stop();
        if (_dumpResponses)
        {
            await _dumper.DumpResponseAsync(
                context.ServerName,
                context.ToolName,
                result,
                _stopwatch.Elapsed,
                context.CancellationToken).ConfigureAwait(false);
        }
        return result;
    }
}
