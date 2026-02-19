# Contributing to OpenOrca

Thank you for your interest in contributing to OpenOrca! This guide will help you get started.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [LM Studio](https://lmstudio.ai/) (for integration tests and local development)
- Git

## Getting Started

```bash
# Clone the repository
git clone https://github.com/openorca-ai/openorca.git
cd openorca

# Build the solution
dotnet build

# Run tests
dotnet test

# Run the CLI
dotnet run --project src/OpenOrca.Cli
```

## Project Structure

```
src/
  OpenOrca.Cli/       # Console application, REPL loop, rendering
  OpenOrca.Core/      # Domain logic (chat, config, sessions, permissions)
  OpenOrca.Tools/     # Tool implementations (31 tools)
tests/
  OpenOrca.Core.Tests/   # Unit tests for Core (49 tests)
  OpenOrca.Tools.Tests/  # Unit tests for Tools (65 tests)
  OpenOrca.Harness/      # Integration tests (requires running LM Studio)
```

## Development Workflow

1. **Create a branch** from `main` for your changes
2. **Write code** following the existing style conventions
3. **Add tests** for new functionality
4. **Run the full test suite**: `dotnet test --configuration Release`
5. **Build in Release mode**: `dotnet build --configuration Release` (must have zero warnings)
6. **Submit a pull request**

## Code Style

- **File-scoped namespaces** (`namespace Foo;` not `namespace Foo { }`)
- **4-space indentation** (no tabs)
- **Nullable reference types** enabled
- **UTF-8** file encoding
- Use `var` for local variables when the type is obvious
- Prefer expression-bodied members for simple methods/properties
- Keep methods focused — if a method exceeds ~50 lines, consider extracting
- Use `sealed` on classes that aren't designed for inheritance

## Adding a New Tool

1. Create a new class under `src/OpenOrca.Tools/` in the appropriate category folder
2. Implement `IOrcaTool` and add the `[OrcaTool("tool_name")]` attribute
3. Define `Name`, `Description`, `RiskLevel`, and `ParameterSchema`
4. Implement `ExecuteAsync`
5. Add unit tests in `tests/OpenOrca.Tools.Tests/`
6. The tool is auto-discovered by `ToolRegistry.DiscoverTools()`

### Risk Levels

- **ReadOnly** — no side effects (reading files, searching, thinking)
- **Moderate** — modifiable but recoverable (writing files, git commit)
- **Dangerous** — potentially destructive (shell execution, git push)

## Running Integration Tests

The `OpenOrca.Harness` project contains integration tests that require a running LM Studio instance:

```bash
# Start LM Studio and load a model first, then:
dotnet run --project tests/OpenOrca.Harness
```

These tests are not run in CI (they require a local LLM server).

## Pull Request Process

1. Ensure all tests pass (`dotnet test --configuration Release`)
2. Ensure the build has zero warnings (`dotnet build --configuration Release`)
3. Write a clear PR description explaining what and why
4. Link any related issues
5. Request review from a maintainer

## Reporting Issues

When reporting a bug, please include:

- OpenOrca version (`openorca --version` or check the binary)
- Operating system and version
- LM Studio version and model being used
- Steps to reproduce
- Relevant log output (`~/.openorca/logs/`)

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md).
