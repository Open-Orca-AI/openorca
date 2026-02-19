# Contributing

Thank you for your interest in contributing to OpenOrca! This guide covers everything you need to get started.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [LM Studio](https://lmstudio.ai/) (for integration tests and local development)
- Git
- A code editor (VS Code, Rider, Visual Studio)

## Development Setup

```bash
# Clone the repository
git clone https://github.com/Open-Orca-AI/openorca.git
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

See [Architecture](Architecture) for a detailed breakdown of every component.

## Code Style

- **File-scoped namespaces** — `namespace Foo;` not `namespace Foo { }`
- **4-space indentation** — no tabs
- **Nullable reference types** enabled
- **UTF-8** file encoding
- Use `var` for local variables when the type is obvious
- Prefer expression-bodied members for simple methods/properties
- Keep methods focused — if a method exceeds ~50 lines, consider extracting
- Use `sealed` on classes that aren't designed for inheritance

## Development Workflow

1. **Create a branch** from `main` for your changes
2. **Write code** following the existing style conventions
3. **Add tests** for new functionality
4. **Run the full test suite:** `dotnet test --configuration Release`
5. **Build in Release mode:** `dotnet build --configuration Release` (must have zero warnings)
6. **Submit a pull request**

## Running Tests

### Unit Tests

```bash
# Run all unit tests
dotnet test

# Run with Release configuration (as CI does)
dotnet test --configuration Release

# Run a specific test project
dotnet test tests/OpenOrca.Core.Tests
dotnet test tests/OpenOrca.Tools.Tests
```

### Integration Tests (Harness)

The harness tests require a running LM Studio instance with a model loaded:

```bash
# Start LM Studio and load a model first, then:
dotnet run --project tests/OpenOrca.Harness
```

The harness runs 7 tests:
1. Connectivity check
2. Simple chat
3. Streaming
4. Native tool calling
5. Text-based tool calling
6. Nudge mechanism
7. Realistic scenario (full system prompt + all 31 tools + real user prompt)

These tests are **not** run in CI (they require a local LLM server).

## Adding a New Tool

See the dedicated [Adding Tools](Adding-Tools) guide for a step-by-step walkthrough with a full code example.

Quick summary:
1. Create a class in `src/OpenOrca.Tools/` under the appropriate category folder
2. Implement `IOrcaTool` and add `[OrcaTool("tool_name")]`
3. Define `Name`, `Description`, `RiskLevel`, and `ParameterSchema`
4. Implement `ExecuteAsync`
5. Add tests in `tests/OpenOrca.Tools.Tests/`
6. The tool is auto-discovered — no registration needed

### Risk Levels

| Level | Use When |
|-------|----------|
| **ReadOnly** | No side effects (reading files, searching, thinking) |
| **Moderate** | Modifiable but recoverable (writing files, git commit) |
| **Dangerous** | Potentially destructive (shell execution, git push) |

## Pull Request Process

1. Ensure all tests pass: `dotnet test --configuration Release`
2. Ensure zero build warnings: `dotnet build --configuration Release`
3. Write a clear PR description explaining **what** and **why**
4. Link any related issues
5. Request review from a maintainer

### PR Description Template

```markdown
## What
Brief description of the change.

## Why
Motivation — what problem does this solve?

## Testing
How was this tested? (unit tests, manual testing, harness results)
```

## CI/CD Pipeline

The project uses GitHub Actions:

| Workflow | Trigger | What It Does |
|----------|---------|-------------|
| `ci.yml` | Push to `main`, PRs to `main` | Build + test on Ubuntu, Windows, macOS. Records demo GIF on push. |
| `release.yml` | Git tags | Builds release binaries for all platforms, creates GitHub Release. |
| `demo.yml` | Demo-related changes | Records the demo GIF. |
| `publish-wiki.yml` | Push to `main` with changes in `wiki/` | Publishes wiki markdown files to the GitHub wiki. |

## Reporting Issues

When reporting a bug, please include:

- **OpenOrca version** — `openorca --version`
- **OS and version**
- **LM Studio version** and model being used
- **Steps to reproduce**
- **Relevant log output** from `~/.openorca/logs/`
- **`/doctor` output**

File issues at: [github.com/Open-Orca-AI/openorca/issues](https://github.com/Open-Orca-AI/openorca/issues)

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](https://github.com/Open-Orca-AI/openorca/blob/main/CODE_OF_CONDUCT.md).
