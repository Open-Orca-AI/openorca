# Custom Commands

OpenOrca supports user-defined slash commands via markdown template files. Create a `.md` file in a commands directory and it becomes a slash command.

## Command Directories

| Location | Scope | Priority |
|----------|-------|----------|
| `.orca/commands/` | Project-level | Checked first |
| `~/.openorca/commands/` | Global | Fallback |

Project-level commands take priority over global commands with the same name. Built-in commands (like `/help`, `/clear`, etc.) always take priority over custom commands.

## Creating a Command

Create a markdown file in one of the command directories. The filename (without `.md`) becomes the command name.

### Example: `/review-pr`

Create `.orca/commands/review-pr.md`:

```markdown
Please review PR #{{ARG1}} thoroughly. Focus on:

1. Code style and consistency
2. Security vulnerabilities
3. Test coverage
4. Performance implications

Additional context: {{ARGS}}
```

### Example: `/deploy`

Create `.orca/commands/deploy.md`:

```markdown
Deploy the application to {{ARG1}} environment.

Steps:
1. Run all tests
2. Build in Release mode
3. Deploy to {{ARG1}}

Extra flags: {{ARG2}}
```

## Template Variables

Templates support the following substitution variables:

| Variable | Description |
|----------|-------------|
| `{{ARGS}}` | All user arguments joined with spaces |
| `{{ARG1}}` | First positional argument |
| `{{ARG2}}` | Second positional argument |
| `{{ARG3}}` | Third positional argument (and so on) |

Variables that don't have a corresponding argument are left as-is in the template.

## Usage

```
> /review-pr 123
> /deploy staging --verbose
> /lint src/
```

When a custom command runs:
1. The template file is loaded
2. `{{ARGS}}` and `{{ARGn}}` variables are substituted
3. The expanded template is injected as a user message
4. The agent loop runs to process it (with full tool access)

## Discovery

Custom commands are discovered when OpenOrca starts. Use `/help` to see available custom commands listed under "Custom Commands".

## Tips

- Use custom commands for repetitive workflows (PR reviews, deployments, code generation patterns)
- Global commands in `~/.openorca/commands/` work across all projects
- Project commands in `.orca/commands/` can be committed to version control for team sharing
- The template is sent as a regular user message, so the LLM has full tool access to execute it
