---
layout: default
title: Home
description: MCP Proxy - Aggregate multiple MCP servers into a single endpoint
---

<div class="hero">
  <h1>MCP Proxy</h1>
  <p>A powerful proxy server for the Model Context Protocol (MCP) that aggregates multiple MCP servers into a single endpoint.</p>
  <div class="hero-buttons">
    <a href="{{ '/getting-started/' | relative_url }}" class="btn btn-primary">Get Started</a>
    <a href="{{ site.github.repository_url }}" class="btn btn-secondary" target="_blank">View on GitHub</a>
  </div>
</div>

## What is MCP Proxy?

MCP Proxy is a .NET-based proxy server that sits between your MCP clients (like Claude Desktop, GitHub Copilot, or OpenCode) and multiple backend MCP servers. It aggregates tools, resources, and prompts from all connected backends, presenting them through a single unified endpoint.

This means you can connect your AI assistant to one proxy and get access to all your MCP servers without configuring each one individually in your client.

## Key Features

<div class="feature-grid">
  <div class="feature-card">
    <h3>Multi-Server Aggregation</h3>
    <p>Connect to multiple MCP servers (STDIO, HTTP, SSE) and expose all their capabilities through a single endpoint.</p>
  </div>
  <div class="feature-card">
    <h3>Flexible Filtering</h3>
    <p>AllowList, DenyList, or use regex patterns to control which tools, resources, and prompts are exposed from each server.</p>
  </div>
  <div class="feature-card">
    <h3>Name Prefixing</h3>
    <p>Add server-specific prefixes to avoid name collisions when aggregating tools from multiple sources.</p>
  </div>
  <div class="feature-card">
    <h3>Hook System</h3>
    <p>Execute custom logic before and after tool calls for logging, transformation, or validation.</p>
  </div>
  <div class="feature-card">
    <h3>Advanced MCP Support</h3>
    <p>Full support for sampling, elicitation, and roots - forward requests between backends and clients seamlessly.</p>
  </div>
  <div class="feature-card">
    <h3>OpenTelemetry Integration</h3>
    <p>Built-in metrics and distributed tracing for observability in production environments.</p>
  </div>
</div>

## Quick Start

Install MCP Proxy as a .NET global tool:

```bash
dotnet tool install -g McpProxy
```

Create a configuration file `mcp-proxy.json`:

```json
{
  "mcp": {
    "filesystem": {
      "type": "stdio",
      "command": "npx",
      "arguments": ["-y", "@anthropic/mcp-server-filesystem", "/workspace"]
    },
    "context7": {
      "type": "sse",
      "url": "https://mcp.context7.com/sse"
    }
  }
}
```

Run the proxy:

```bash
mcpproxy stdio ./mcp-proxy.json
```

That's it! Your MCP client can now connect to the proxy and access tools from both servers.

<a href="{{ '/getting-started/' | relative_url }}" class="btn btn-primary">Read the full Getting Started guide</a>

## Architecture

```
┌─────────────────┐
│   MCP Client    │  (Claude Desktop, OpenCode, GitHub Copilot)
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│   MCP Proxy     │  Aggregation + Filtering + Hooks
└────────┬────────┘
         │
    ┌────┴────┬────────────┐
    │         │            │
    ▼         ▼            ▼
┌───────┐ ┌───────┐   ┌───────┐
│Server1│ │Server2│   │Server3│  (STDIO, HTTP, SSE)
└───────┘ └───────┘   └───────┘
```

## Why MCP Proxy?

- **Simplified Client Configuration**: Configure one proxy instead of multiple servers in each client
- **Centralized Control**: Apply consistent filtering, logging, and security policies
- **Collision Prevention**: Automatic prefixing prevents tool name conflicts
- **Observability**: OpenTelemetry integration for monitoring and debugging
- **Flexibility**: Works with any MCP-compatible client and server

## Documentation

- [Getting Started]({{ '/getting-started/' | relative_url }}) - Installation and basic setup
- [Configuration Reference]({{ '/configuration/' | relative_url }}) - Complete configuration options
- [Features]({{ '/features/' | relative_url }}) - Filtering, prefixing, and hooks
- [Advanced]({{ '/advanced/' | relative_url }}) - Telemetry, caching, and notifications
- [Client Integrations]({{ '/integrations/' | relative_url }}) - Setup guides for popular MCP clients
- [API Reference]({{ '/api/' | relative_url }}) - Programmatic usage

## License

MCP Proxy is open source software released under the [MIT License](https://github.com/MoaidHathot/mcp-proxy/blob/main/LICENSE).
