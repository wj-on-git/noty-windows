using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;
using Noty.Core;

namespace Noty.Services;

// MARK: - Archive format

/// The same JSON the macOS build writes, so a `.stickies` archive moves between
/// the two platforms untouched.
public sealed class StickyArchive
{
    public int Version { get; set; } = 1;
    public string App { get; set; } = "Noty";
    public DateTime Exported { get; set; } = DateTime.Now;
    public List<StickyNote> Notes { get; set; } = new();
}

public sealed class StickyNote
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public int Color { get; set; }
    public string ColorName { get; set; } = "";
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public bool Archived { get; set; }
    public double Order { get; set; }

    public StickyNote() { }

    public StickyNote(Note n)
    {
        Id = n.Id;
        Title = n.Title;
        Body = n.Body;
        Color = n.Color;
        ColorName = n.Palette.Name;
        Created = n.Created;
        Modified = n.Modified;
        Archived = n.Archived;
        Order = n.Order;
    }

    public Note ToNote() => new()
    {
        Id = Id,
        Title = string.IsNullOrEmpty(Title) ? Note.DerivedTitle(Body) : Title,
        Body = Body,
        Color = Color,
        Created = Created,
        Modified = Modified,
        Archived = Archived,
        Order = Order,
    };
}

// MARK: - Export / import

public static class Transfer
{
    public enum Format { Markdown, PlainText, SingleFile, Stickies }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static void Export(Format format, IReadOnlyList<Note> notes)
    {
        if (notes.Count == 0)
        {
            Alert("没有可导出的内容", "目前还没有任何便签。");
            return;
        }
        switch (format)
        {
            case Format.Markdown: PerFile(notes, "md", MarkdownBody); break;
            case Format.PlainText: PerFile(notes, "txt", n => n.Body); break;
            case Format.SingleFile: SingleFile(notes); break;
            case Format.Stickies: Archive(notes); break;
        }
    }

    // One file per note, into a folder the user picks.
    private static void PerFile(IReadOnlyList<Note> notes, string ext, Func<Note, string> render)
    {
        var picker = new OpenFolderDialog
        {
            Title = $"选择用于保存 {notes.Count} 个 {ext.ToUpperInvariant()} 文件的文件夹",
        };
        if (picker.ShowDialog() != true) return;
        var dir = picker.FolderName;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var written = 0;
        foreach (var n in notes)
        {
            var name = SafeName(n);
            var candidate = name;
            var i = 2;
            while (!used.Add(candidate)) candidate = $"{name}-{i++}";
            try
            {
                File.WriteAllText(Path.Combine(dir, $"{candidate}.{ext}"), render(n), Encoding.UTF8);
                written++;
            }
            catch (Exception e)
            {
                Log.Line($"export failed for {candidate}.{ext}: {e.Message}");
            }
        }
        Reveal(dir);
        if (written < notes.Count)
            Alert("导出未完成", $"已写入 {written}/{notes.Count} 篇便签。详情见 noty.log。");
    }

    private static void SingleFile(IReadOnlyList<Note> notes)
    {
        var panel = new SaveFileDialog
        {
            FileName = $"Noty-{Fmt.FileStamp(DateTime.Now)}.md",
            Filter = "Markdown (*.md)|*.md|文本 (*.txt)|*.txt|所有文件 (*.*)|*.*",
        };
        if (panel.ShowDialog() != true) return;

        var doc = string.Join("\n\n---\n\n", notes.Select(n =>
            $"## {n.DisplayTitle}\n" +
            $"<!-- {n.Palette.Name} · 创建于 {Fmt.Stamp(n.Created)} · " +
            $"修改于 {Fmt.Stamp(n.Modified)}{(n.Archived ? " · 已归档" : "")} -->\n\n" +
            TaskSyntax.ToMarkdown(n.Body)));

        var header = $"# Noty 导出\n\n{notes.Count} 篇便签 · {Fmt.Stamp(DateTime.Now)}\n\n---\n\n";
        Write(header + doc, panel.FileName);
    }

    private static void Archive(IReadOnlyList<Note> notes)
    {
        var panel = new SaveFileDialog
        {
            FileName = $"Noty-{Fmt.FileStamp(DateTime.Now)}.stickies",
            Filter = "便签归档 (*.stickies)|*.stickies|所有文件 (*.*)|*.*",
        };
        if (panel.ShowDialog() != true) return;

        var archive = new StickyArchive { Notes = notes.Select(n => new StickyNote(n)).ToList() };
        Write(JsonSerializer.Serialize(archive, Json), panel.FileName);
    }

    public static void Import()
    {
        var panel = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Noty 归档与便签|*.stickies;*.md;*.txt|" +
                     "便签归档 (*.stickies)|*.stickies|文本与 Markdown|*.md;*.txt",
        };
        if (panel.ShowDialog() != true) return;

        var incoming = new List<Note>();
        foreach (var path in panel.FileNames)
        {
            try
            {
                var text = File.ReadAllText(path);
                if (Path.GetExtension(path).Equals(".stickies", StringComparison.OrdinalIgnoreCase))
                {
                    var archive = JsonSerializer.Deserialize<StickyArchive>(text, Json);
                    if (archive is not null)
                        incoming.AddRange(archive.Notes.Select(s => s.ToNote()));
                }
                else
                {
                    var body = TaskSyntax.FromMarkdown(text);
                    incoming.Add(new Note
                    {
                        Body = body,
                        Title = Note.DerivedTitle(body),
                        Color = incoming.Count % NoteColor.All.Count,
                    });
                }
            }
            catch (Exception e)
            {
                Log.Line($"import failed for {path}: {e.Message}");
            }
        }

        var added = NoteStore.Shared.Ingest(incoming);
        Alert("导入完成", added == 0
            ? "未能从这些文件中读取任何内容。"
            : $"已添加 {added} 篇便签。");
    }

    private static string MarkdownBody(Note n)
    {
        var header = $"# {n.DisplayTitle}\n\n" +
                     $"<!-- {n.Palette.Name} · 创建于 {Fmt.Stamp(n.Created)} · " +
                     $"修改于 {Fmt.Stamp(n.Modified)}{(n.Archived ? " · 已归档" : "")} -->\n\n";
        // The first line is already the title, so it is not repeated in the body.
        var lines = n.Body.Split('\n').Skip(1);
        return header + TaskSyntax.ToMarkdown(string.Join("\n", lines)).TrimStart('\n');
    }

    private static string SafeName(Note n)
    {
        var title = string.IsNullOrWhiteSpace(n.Title) ? "便签" : n.Title;
        var clean = Regex.Replace(title, @"[\\/:*?""<>|\r\n\t]", " ").Trim();
        clean = Regex.Replace(clean, @"\s+", " ");
        if (clean.Length > 48) clean = clean[..48].Trim();
        if (clean.Length == 0) clean = "便签";
        return $"{clean} {Fmt.FileStamp(n.Created)}";
    }

    private static void Write(string text, string path)
    {
        try
        {
            File.WriteAllText(path, text, Encoding.UTF8);
            Reveal(Path.GetDirectoryName(path) ?? path);
        }
        catch (Exception e)
        {
            Alert("导出失败", e.Message);
        }
    }

    private static void Reveal(string dir)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch { /* opening Explorer is a courtesy, not a requirement */ }
    }

    private static void Alert(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
