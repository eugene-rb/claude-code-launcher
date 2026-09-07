using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Services;

/// <summary>Creates a provider-neutral checkpoint that Claude Code and Codex can both read when a
/// task moves between them. Checkpoints live under AppData, not in the project, so they cannot be
/// committed accidentally.</summary>
public sealed class SharedTaskContextService(string? rootOverride = null)
{
    private const int TranscriptHeadBytes = 256 * 1024;
    private const int TranscriptTailBytes = 1024 * 1024;
    private const int MaxMessages = 16;
    private const int MaxMessageChars = 6000;
    private const int MaxOriginalTaskChars = 4000;

    /// <summary>Heading of the section that carries the task's original request forward, verbatim,
    /// through every handoff. See <see cref="ExtractOriginalTask"/> for why it has to be carried
    /// rather than re-derived.</summary>
    public const string OriginalTaskHeading = "## 元のタスク";

    private const string RecentConversationHeading = "## Recent conversation";

    private readonly string _root = rootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeLauncher", "shared-context");

    public string Capture(string workingDirectory, AgentKind sourceAgent)
    {
        Directory.CreateDirectory(_root);
        var checkpointPath = GetCheckpointPath(workingDirectory);
        var transcriptPath = AgentTranscriptSourceRegistry.Get(sourceAgent)
            .FindMostRecentTranscriptFile(workingDirectory);
        var messages = transcriptPath is null
            ? []
            : ExtractMessages(ReadTranscriptExcerpts(transcriptPath));

        var originalTask = CarryForwardOriginalTask(checkpointPath, messages);

        var builder = new StringBuilder()
            .AppendLine("# Shared task checkpoint")
            .AppendLine()
            .AppendLine($"- Captured: {DateTimeOffset.Now:O}")
            .AppendLine($"- Source agent: {AgentCatalog.Get(sourceAgent).DisplayName}")
            .AppendLine($"- Working directory: {Path.GetFullPath(workingDirectory)}")
            .AppendLine($"- Source transcript: {(transcriptPath is null ? "not found" : Path.GetFileName(transcriptPath))}")
            .AppendLine()
            .AppendLine("The working tree is the source of truth. Inspect its current files and git diff before changing anything. Continue the unfinished task; do not restart completed work.")
            .AppendLine()
            .AppendLine(OriginalTaskHeading)
            .AppendLine()
            .AppendLine(originalTask)
            .AppendLine()
            .AppendLine(RecentConversationHeading);

        if (messages.Count == 0)
        {
            builder.AppendLine().AppendLine("No readable conversation messages were found. Recover state from the working tree.");
        }
        else
        {
            foreach (var message in messages)
            {
                builder.AppendLine().AppendLine($"### {message.Role}").AppendLine().AppendLine(message.Text);
            }
        }

        var tempPath = checkpointPath + ".tmp";
        File.WriteAllText(tempPath, builder.ToString(), new UTF8Encoding(false));
        File.Move(tempPath, checkpointPath, overwrite: true);
        return checkpointPath;
    }

    /// <summary>Returns the original request to write into this capture: the one already carried by
    /// the previous checkpoint if there is one, otherwise the earliest user message in the transcript
    /// just read.</summary>
    private static string CarryForwardOriginalTask(string checkpointPath, IReadOnlyList<CheckpointMessage> messages)
    {
        string? task = null;
        try
        {
            if (File.Exists(checkpointPath))
            {
                task = ExtractOriginalTask(File.ReadAllText(checkpointPath));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Fall through to deriving it from the transcript.
        }

        task ??= messages.FirstOrDefault(message => message.Role == "User")?.Text;
        if (string.IsNullOrWhiteSpace(task))
        {
            return "(元の依頼を特定できませんでした。作業ツリーと git log から目的を復元してください。)";
        }

        var trimmed = task.Trim();
        return trimmed.Length > MaxOriginalTaskChars ? trimmed[..MaxOriginalTaskChars] + "…" : trimmed;
    }

    /// <summary>Reads the <see cref="OriginalTaskHeading"/> section out of an existing checkpoint, or
    /// null if it has none (a checkpoint written before this section existed).
    ///
    /// <para>This is what stops a long unattended run from forgetting what it was asked to do. The
    /// checkpoint file is rewritten in place on every handoff, and each capture reads the transcript of
    /// the agent that was just running - whose <em>first</em> user message is the continuation prompt
    /// telling it to go read this very file. So from the second handoff on, deriving the task from the
    /// transcript alone yields "read the checkpoint", and the actual request is gone. Carrying the
    /// section forward verbatim keeps the original intact no matter how many times the task changes
    /// hands, which is exactly the case a multi-day job hits.</para></summary>
    public static string? ExtractOriginalTask(string checkpointText)
    {
        var lines = checkpointText.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, line => line.Trim() == OriginalTaskHeading);
        if (start < 0)
        {
            return null;
        }

        var body = new StringBuilder();
        for (var i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            body.AppendLine(lines[i]);
        }

        var text = body.ToString().Trim();
        return text.Length == 0 ? null : text;
    }

    public static string BuildContinuationPrompt(string checkpointPath, AgentKind sourceAgent)
    {
        var source = AgentCatalog.Get(sourceAgent).DisplayName;
        return $"{source} から未完了タスクを引き継ぎます。共有チェックポイント「{checkpointPath}」を最初に読み、次に作業ツリーと git diff を確認してください。完了済みの作業をやり直さず、中断地点から自律的に続行し、必要な検証まで完了してください。";
    }

    /// <summary>Handed to a same-agent auto-resume as the CLI's positional prompt argument
    /// (<c>claude -c "…"</c> / <c>codex resume --last "…"</c>, both verified to accept one). A resume
    /// reopens the previous conversation at an idle prompt - it does not carry on with what the usage
    /// limit interrupted - so something has to restart it. Passing it as a launch argument is what
    /// makes an unattended resume work identically on Claude Code and Codex, and it succeeds where
    /// <see cref="ConsoleInputInjector"/> can't: that path is ASCII-only by construction, so it could
    /// never have delivered this instruction in the first place.</summary>
    public const string ResumeContinuationPrompt =
        "利用上限による中断から復帰しました。作業ツリーと git diff で現在地を確認し、完了済みの作業をやり直さず、中断地点から自律的に続行して、必要な検証まで完了してください。";

    public static AgentKind? GetCounterpart(AgentKind sourceAgent) => sourceAgent switch
    {
        AgentKind.ClaudeCode => AgentKind.CodexCli,
        AgentKind.CodexCli => AgentKind.ClaudeCode,
        _ => null,
    };

    private string GetCheckpointPath(string workingDirectory)
    {
        var normalized = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
        return Path.Combine(_root, $"{hash}.md");
    }

    public static IReadOnlyList<CheckpointMessage> ExtractMessages(string? jsonlText)
    {
        if (string.IsNullOrWhiteSpace(jsonlText))
        {
            return [];
        }

        var messages = new List<CheckpointMessage>();
        foreach (var rawLine in jsonlText.Split('\n'))
        {
            if (TryExtractMessage(rawLine.Trim()) is not { } message)
            {
                continue;
            }

            var text = Collapse(message.Text);
            if (text.Length == 0)
            {
                continue;
            }

            if (text.Length > MaxMessageChars)
            {
                text = text[..MaxMessageChars] + "…";
            }

            messages.Add(message with { Text = text });
        }

        if (messages.Count <= MaxMessages)
        {
            return messages;
        }

        // Preserve the task's first user request even when a long tool-heavy session pushed it far
        // outside the tail, then combine it with the most recent exchange.
        var firstUser = messages.FirstOrDefault(message => message.Role == "User");
        var recent = messages.TakeLast(firstUser is null ? MaxMessages : MaxMessages - 1).ToList();
        if (firstUser is not null && !recent.Contains(firstUser))
        {
            recent.Insert(0, firstUser);
        }

        return recent;
    }

    private static string? ReadTranscriptExcerpts(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length <= TranscriptHeadBytes + TranscriptTailBytes)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }

            var headBuffer = new byte[TranscriptHeadBytes];
            var headRead = stream.Read(headBuffer, 0, headBuffer.Length);
            stream.Seek(-TranscriptTailBytes, SeekOrigin.End);
            var tailBuffer = new byte[TranscriptTailBytes];
            var tailRead = stream.Read(tailBuffer, 0, tailBuffer.Length);
            return Encoding.UTF8.GetString(headBuffer, 0, headRead)
                + "\n"
                + Encoding.UTF8.GetString(tailBuffer, 0, tailRead);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static CheckpointMessage? TryExtractMessage(string line)
    {
        if (line.Length == 0)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            JsonElement message = root;

            if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty("role", out _))
            {
                message = payload;
            }
            else if (root.TryGetProperty("message", out var nested) && nested.ValueKind == JsonValueKind.Object)
            {
                message = nested;
            }

            if (!message.TryGetProperty("role", out var roleElement) || roleElement.ValueKind != JsonValueKind.String
                || !message.TryGetProperty("content", out var content))
            {
                return null;
            }

            var role = roleElement.GetString();
            if (role is not ("user" or "assistant"))
            {
                return null;
            }

            var text = ExtractText(content);
            return string.IsNullOrWhiteSpace(text)
                ? null
                : new CheckpointMessage(role == "user" ? "User" : "Assistant", text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object
                || !block.TryGetProperty("type", out var typeElement)
                || typeElement.GetString() is not ("text" or "input_text" or "output_text"))
            {
                continue;
            }

            var key = block.TryGetProperty("text", out var textElement) ? "text"
                : block.TryGetProperty("content", out textElement) ? "content" : null;
            if (key is not null && textElement.ValueKind == JsonValueKind.String)
            {
                if (builder.Length > 0) builder.AppendLine();
                builder.Append(textElement.GetString());
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static string Collapse(string value) => value.Trim().Replace("\r\n", "\n");
}

public sealed record CheckpointMessage(string Role, string Text);
