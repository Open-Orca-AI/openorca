namespace OpenOrca.Cli.Repl;

/// <summary>
/// Constant prompt templates used by the agent loop for nudging, tool call formatting,
/// plan mode instructions, and truncation recovery.
/// </summary>
internal static class PromptConstants
{
    /// <summary>
    /// System prompt addition when plan mode is active.
    /// </summary>
    public const string PlanModeSystemPromptAddition = """
        *** PLAN MODE IS ACTIVE ***

        You are currently in PLAN MODE. In this mode you MUST NOT make any changes.

        ALLOWED actions:
        - Use read-only tools (read_file, list_directory, glob, grep, think, web_fetch, web_search,
          get_process_output, task_list) to explore and understand the codebase
        - Describe your plan step by step in your response text

        BLOCKED actions:
        - Writing, editing, or deleting files (write_file, edit_file, delete_file, move_file, copy_file)
        - Running commands (bash, git_commit, git_push, git_pull, git_checkout, git_branch, git_stash)
        - Any tool that modifies state

        YOUR TASK in plan mode:
        1. Explore the codebase using read-only tools to understand what exists
        2. Produce a clear, numbered plan describing:
           - What files you would create, modify, or delete
           - What specific changes you would make (include code snippets where helpful)
           - What commands you would run
           - What verification steps you would take
        3. Be specific: include file paths, function names, and line numbers
        4. The user will review your plan and can approve, modify, or discard it
        """;

    /// <summary>
    /// Sent when a tool call was truncated (model hit token limit mid-generation).
    /// </summary>
    public const string TruncatedToolCallMessage = """
        Your previous response was cut off mid-tool-call (the <tool_call> tag was never closed).
        This means your output exceeded the token limit.

        To avoid this, follow these rules:
        1. NEVER rewrite an entire file with write_file if you can use edit_file instead.
        2. If you must create a new file, write a SHORT skeleton first (under 100 lines), then use edit_file to add sections incrementally.
        3. Keep each tool call's arguments under ~3000 characters.
        4. Break large tasks into multiple small tool calls rather than one large one.

        Resume your task now using smaller tool calls.
        """;

    /// <summary>
    /// Sent when the model output code/actions without using tool_call tags.
    /// </summary>
    public const string NudgeMessage = """
        IMPORTANT: You showed code or described an action but did NOT use <tool_call> tags.
        Your response will NOT be executed unless you wrap tool calls in <tool_call> tags.

        Please re-do your response. Instead of showing code in markdown blocks, use the appropriate tool:
        - To create/write a file: <tool_call>{"name": "write_file", "arguments": {"path": "...", "content": "..."}}</tool_call>
        - To edit a file: <tool_call>{"name": "edit_file", "arguments": {"path": "...", "old_string": "...", "new_string": "..."}}</tool_call>
        - To run a command: <tool_call>{"name": "bash", "arguments": {"command": "..."}}</tool_call>
        - To read a file: <tool_call>{"name": "read_file", "arguments": {"path": "..."}}</tool_call>

        You MUST output <tool_call> tags to take action. Do it now.
        """;

    /// <summary>
    /// Tool call format instructions included in the system prompt for text-based tool calling.
    /// </summary>
    public const string ToolCallFormatInstructions = """
        RESPONSE FORMAT:
        If you want to think/reason, use <think>...</think> tags.
        After thinking, you MUST output <tool_call> tags to take action.
        Example response:
        <think>The user wants a new file. I'll use write_file to create it.</think>
        <tool_call>
        {"name": "write_file", "arguments": {"path": "test.txt", "content": "hello world"}}
        </tool_call>

        HOW TO CALL TOOLS:
        To use a tool, output a tool call in this EXACT format using <tool_call> tags.
        You MUST place <tool_call> tags OUTSIDE of any <think> blocks — they must appear in your visible output.

        Format:
        <tool_call>
        {"name": "tool_name", "arguments": {"param1": "value1", "param2": "value2"}}
        </tool_call>

        Examples:
        <tool_call>
        {"name": "mkdir", "arguments": {"path": "my_project"}}
        </tool_call>

        <tool_call>
        {"name": "write_file", "arguments": {"path": "my_project/hello.txt", "content": "Hello World!"}}
        </tool_call>

        <tool_call>
        {"name": "bash", "arguments": {"command": "ls -la"}}
        </tool_call>

        <tool_call>
        {"name": "read_file", "arguments": {"path": "src/main.py"}}
        </tool_call>

        <tool_call>
        {"name": "glob", "arguments": {"pattern": "**/*.cs"}}
        </tool_call>

        RULES FOR TOOL CALLS:
        - <tool_call> tags MUST appear in your response text, OUTSIDE of <think> blocks.
        - If you use <think>...</think> for reasoning, put your <tool_call> tags AFTER </think>.
        - You may call multiple tools by including multiple <tool_call> blocks in a single response. When tools are independent, include them all at once for parallel execution.
        - After all tool results are returned, you can make further calls if needed.
        - NEVER just show code — use write_file to create files, bash to run commands.
        - Every response where you need to act MUST contain at least one <tool_call>.
        - ALWAYS include a "_reason" field in your tool call arguments — a short, human-readable explanation of WHY you are calling this tool (e.g., "Check what files exist in the src folder", "Fix the typo on line 42"). This is displayed to the user and is stripped before execution.
        """;

    /// <summary>
    /// Injected as a user-role message after all tool calls complete to get a clean summary.
    /// </summary>
    public const string SummaryRequestMessage = """
        All tool calls are complete. Provide a final summary of what you did.
        Use markdown formatting (headers, bold, bullets, inline code) directly in your response — do NOT wrap it in a code fence.
        Structure:
        - **Summary**: 1–3 sentences describing what you did overall.
        - **Changes made**: A bulleted list of files created, modified, or deleted, with brief descriptions.
        - **Key findings**: Any notable discoveries, warnings, or issues encountered (omit if none).
        - **Suggested next steps**: Actionable follow-ups the user might want to do (omit if none).
        Keep it concise. Use `code` formatting for file paths and commands. Do NOT call any tools.
        """;

    /// <summary>
    /// Injected as a user-role message when a tool has failed 3+ times identically.
    /// </summary>
    public const string RetryLoopRedirectMessage =
        "SYSTEM NOTICE: A tool has now failed 3 times in a row with the exact same error. " +
        "You MUST NOT call that tool again with the same arguments. " +
        "Read the error message and either: (1) fix the underlying problem first (e.g. run 'git init' with the bash tool if this is not a git repository), " +
        "or (2) skip that operation entirely and move on. " +
        "Do something DIFFERENT on your next turn.";
}
