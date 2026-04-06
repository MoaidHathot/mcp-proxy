using System.Diagnostics;

namespace McpProxy.SDK.Telemetry;

/// <summary>
/// OpenTelemetry activity source for distributed tracing in the MCP proxy.
/// </summary>
public sealed class ProxyActivitySource : IDisposable
{
    /// <summary>
    /// The name of the activity source.
    /// </summary>
    public const string SourceName = "McpProxy";

    private readonly ActivitySource _activitySource;

    /// <summary>
    /// Initializes a new instance of <see cref="ProxyActivitySource"/>.
    /// </summary>
    /// <param name="version">The service version.</param>
    public ProxyActivitySource(string? version = null)
    {
        _activitySource = new ActivitySource(SourceName, version ?? "1.0.0");
    }

    /// <summary>
    /// Gets the underlying activity source.
    /// </summary>
    public ActivitySource ActivitySource => _activitySource;

    /// <summary>
    /// Starts an activity for a tool call.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="toolName">The tool name.</param>
    /// <returns>The started activity, or null if not sampled.</returns>
    public Activity? StartToolCall(string serverName, string toolName)
    {
        var activity = _activitySource.StartActivity("mcpproxy.tool_call", ActivityKind.Client);
        if (activity is not null)
        {
            activity.SetTag("mcp.server", serverName);
            activity.SetTag("mcp.tool", toolName);
            activity.SetTag("mcp.operation", "tool_call");
        }
        return activity;
    }

    /// <summary>
    /// Starts an activity for a resource read.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="resourceUri">The resource URI.</param>
    /// <returns>The started activity, or null if not sampled.</returns>
    public Activity? StartResourceRead(string serverName, string resourceUri)
    {
        var activity = _activitySource.StartActivity("mcpproxy.resource_read", ActivityKind.Client);
        if (activity is not null)
        {
            activity.SetTag("mcp.server", serverName);
            activity.SetTag("mcp.resource", resourceUri);
            activity.SetTag("mcp.operation", "resource_read");
        }
        return activity;
    }

    /// <summary>
    /// Starts an activity for a prompt get.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="promptName">The prompt name.</param>
    /// <returns>The started activity, or null if not sampled.</returns>
    public Activity? StartPromptGet(string serverName, string promptName)
    {
        var activity = _activitySource.StartActivity("mcpproxy.prompt_get", ActivityKind.Client);
        if (activity is not null)
        {
            activity.SetTag("mcp.server", serverName);
            activity.SetTag("mcp.prompt", promptName);
            activity.SetTag("mcp.operation", "prompt_get");
        }
        return activity;
    }

    /// <summary>
    /// Starts an activity for listing tools.
    /// </summary>
    /// <returns>The started activity, or null if not sampled.</returns>
    public Activity? StartListTools()
    {
        var activity = _activitySource.StartActivity("mcpproxy.list_tools", ActivityKind.Server);
        activity?.SetTag("mcp.operation", "list_tools");
        return activity;
    }

    /// <summary>
    /// Starts an activity for listing resources.
    /// </summary>
    /// <returns>The started activity, or null if not sampled.</returns>
    public Activity? StartListResources()
    {
        var activity = _activitySource.StartActivity("mcpproxy.list_resources", ActivityKind.Server);
        activity?.SetTag("mcp.operation", "list_resources");
        return activity;
    }

    /// <summary>
    /// Starts an activity for listing prompts.
    /// </summary>
    /// <returns>The started activity, or null if not sampled.</returns>
    public Activity? StartListPrompts()
    {
        var activity = _activitySource.StartActivity("mcpproxy.list_prompts", ActivityKind.Server);
        activity?.SetTag("mcp.operation", "list_prompts");
        return activity;
    }

    /// <summary>
    /// Records an error on an activity.
    /// </summary>
    /// <param name="activity">The activity.</param>
    /// <param name="exception">The exception.</param>
    public static void RecordError(Activity? activity, Exception exception)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("error.type", exception.GetType().Name);
        activity.SetTag("error.message", exception.Message);
        activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message },
            { "exception.stacktrace", exception.StackTrace }
        }));
    }

    /// <summary>
    /// Records success on an activity.
    /// </summary>
    /// <param name="activity">The activity.</param>
    public static void RecordSuccess(Activity? activity)
    {
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _activitySource.Dispose();
    }
}
