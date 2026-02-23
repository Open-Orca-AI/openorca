# Project Instructions (ORCA.md)

ORCA.md is a markdown file you place in your project root to give OpenOrca persistent, project-specific instructions. It's loaded automatically every session and injected into the system prompt.

## What Is It?

Think of ORCA.md as a `.editorconfig` for your AI assistant. It tells OpenOrca:
- What your project is and how it's structured
- Coding conventions and preferences
- What to do (and not do) when making changes
- Important context that would otherwise need repeating every session

## File Locations

OpenOrca looks for instructions in this order:

1. `.orca/ORCA.md` — hidden directory in the project root
2. `ORCA.md` — directly in the project root

The project root is determined by walking up from the current working directory looking for common root markers (`.git`, `.sln`, `package.json`, etc.).

## Managing ORCA.md

### Quick Setup with `/init`

The fastest way to create project instructions:

```
> /init
```

This creates `.orca/ORCA.md` with a starter template containing sections for overview, architecture, code style, testing, and common commands. Fill in the sections relevant to your project.

If an ORCA.md already exists, `/init` will tell you and suggest using `/memory edit` instead.

### View Current Instructions

```
> /memory
> /memory show
```

Shows the contents of your ORCA.md in a formatted panel. If no file exists, it tells you where to create one.

### Edit Instructions

```
> /memory edit
```

Opens ORCA.md in your default editor:
- Windows: `notepad`
- Linux/macOS: `nano`
- Override with the `EDITOR` environment variable

If no ORCA.md exists, it creates one with a starter template.

## What to Put in ORCA.md

### Project Overview
```markdown
# Project Instructions

This is a Node.js REST API using Express and PostgreSQL.
```

### Coding Conventions
```markdown
## Conventions
- Use TypeScript strict mode
- Use async/await, never callbacks
- All API responses use the `{ data, error, meta }` envelope
- Tests use Jest with supertest for API routes
```

### File Structure Context
```markdown
## Structure
- `src/routes/` — Express route handlers
- `src/services/` — Business logic
- `src/models/` — Sequelize models
- `src/middleware/` — Express middleware
```

### Do's and Don'ts
```markdown
## Rules
- Always add tests for new routes
- Never modify migration files after they've been run
- Use environment variables for all config (never hardcode)
- Run `npm run lint` before committing
```

### Common Tasks
```markdown
## Common Tasks
- To add a new API endpoint: create route in `src/routes/`, service in `src/services/`, add tests
- To add a new model: create migration with `npx sequelize migration:create`, then model file
- To run tests: `npm test`
- To start dev server: `npm run dev`
```

## Full Example

```markdown
# Project Instructions

OpenOrca — a .NET 10 CLI AI orchestrator.

## Architecture
- `src/OpenOrca.Cli` — Console app, REPL, streaming renderer
- `src/OpenOrca.Core` — Domain logic (chat, config, sessions, permissions)
- `src/OpenOrca.Tools` — 35 tool implementations

## Conventions
- File-scoped namespaces
- 4-space indentation, no tabs
- Nullable reference types enabled
- Use `sealed` on classes not designed for inheritance
- Keep methods under 50 lines

## Testing
- Unit tests: `dotnet test`
- Integration tests: `dotnet run --project tests/OpenOrca.Harness` (requires LM Studio)
- All new tools need tests in `tests/OpenOrca.Tools.Tests/`

## Rules
- File-based logging only (no console debug output)
- Errors should show the log path hint
- Never break the `IOrcaTool` interface contract
```

## How It's Used

The ORCA.md content is injected into the system prompt via the `{{PROJECT_INSTRUCTIONS}}` template variable. The model sees it as part of its instructions for every message in the conversation.

This means:
- Instructions are always active — no need to repeat them
- Changes take effect on the next `/clear` or new session (when the system prompt is rebuilt)
- Keep it concise — it uses context window space
