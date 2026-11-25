namespace Noty.Core;

public static class Fmt
{
    public static string Stamp(DateTime d) => d.ToString("yyyy年M月d日 HH:mm");
    public static string FileStamp(DateTime d) => d.ToString("yyyy-MM-dd-HHmmss");

    public static string Ago(DateTime d)
    {
        var span = DateTime.Now - d;
        if (span.TotalSeconds < 60) return "刚刚";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} 分钟前";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} 小时前";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays} 天前";
        return Stamp(d);
    }
}
