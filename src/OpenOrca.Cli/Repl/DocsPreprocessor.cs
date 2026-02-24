using System.Text.RegularExpressions;
using Spectre.Console;

namespace OpenOrca.Cli.Repl;

/// <summary>
/// Preprocesses user input to expand @docs:library references into inline documentation
/// fetched via Context7. Similar to <see cref="InputPreprocessor"/> for @file references.
/// </summary>
internal sealed partial class DocsPreprocessor
{
    private const int MaxDocsChars = 30_000;
    private readonly Context7Helper _context7;

    public DocsPreprocessor(Context7Helper context7)
    {
        _context7 = context7;
    }

    // Matches @docs:react, @docs:@angular/core, @docs:next.js, etc.
    // The lookbehind ensures it's preceded by whitespace or start-of-line.
    [GeneratedRegex(@"(?<=^|\s)@docs:([\w./@-]+)", RegexOptions.Compiled)]
    private static partial Regex DocsReferencePattern();

    /// <summary>
    /// Expand all @docs:library references in the input. For each match, fetches docs
    /// via Context7 and replaces the token with inline documentation.
    /// Returns the (possibly expanded) input.
    /// </summary>
    public async Task<string> ExpandDocsReferencesAsync(string input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.Contains("@docs:"))
            return input;

        if (!_context7.IsAvailable())
            return input;

        var matches = DocsReferencePattern().Matches(input);
        if (matches.Count == 0)
            return input;

        var result = input;
        var fetchedLibraries = new List<string>();
        var failedLibraries = new List<string>();

        // Process in reverse order to preserve string offsets
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            var library = match.Groups[1].Value;

            try
            {
                var docs = await _context7.FetchDocsAsync(library, null, ct);

                if (docs is null)
                {
                    failedLibraries.Add(library);
                    continue;
                }

                if (docs.Length > MaxDocsChars)
                    docs = docs[..MaxDocsChars] + "\n... (truncated)";

                var replacement = $"[Documentation for {library} via Context7]\n{docs}\n[/Documentation]";
                result = result[..match.Index] + replacement + result[(match.Index + match.Length)..];
                fetchedLibraries.Add(library);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                failedLibraries.Add(library);
            }
        }

        if (fetchedLibraries.Count > 0)
        {
            fetchedLibraries.Reverse(); // Back to original order
            AnsiConsole.MarkupLine($"[green]Fetched docs: {Markup.Escape(string.Join(", ", fetchedLibraries))}[/]");
        }

        if (failedLibraries.Count > 0)
        {
            failedLibraries.Reverse();
            AnsiConsole.MarkupLine($"[yellow]Failed to fetch docs: {Markup.Escape(string.Join(", ", failedLibraries))}[/]");
        }

        return result;
    }
}
