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
            .AppendLine("## Recent conversation");

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

    public static string BuildContinuationPrompt(string checkpointPath, AgentKind sourceAgent)
    {
        var source = AgentCatalog.Get(sourceAgent).DisplayName;
        return $"{source} から未完了タスクを引き継ぎます。共有チェックポイント「{checkpointPath}」を最初に読み、次に作業ツリーと git diff を確認してください。完了済みの作業をやり直さず、中断地点から自律的に続行し、必要な検証まで完了してください。";
    }

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
