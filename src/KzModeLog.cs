using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FysZeRewardOverlay;

internal sealed class KzModeLog(string path)
{
    private const int MaxReadBytes = 64 * 1024;
    private const string LogPrefix = @"^\s*(?:\d{2}/\d{2}\s+\d{2}:\d{2}:\d{2}\s+)?(?:\[[^\]\r\n]+\]\s*)*";
    private const string ModeName = @"(?<mode>Classic|Vanilla|CKZ|VNL)";
    private static readonly Regex ModeMessage = new(LogPrefix + "(?:" +
        @"You have switched to the\s+" + ModeName + @"\s+mode\." + "|" +
        @"你已(?:切换到|切換至)\s*" + ModeName + @"\s*模式[.。]" + "|" +
        @"(?:Possible modes:\s*\(Current mode is|可用模式[:：]\s*[（(](?:当前模式为|目前模式為))\s+" + ModeName + @"[)）])\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ConsoleMap = new(LogPrefix + @"Map:\s*""(?<map>(?:workshop/\d+/)?[a-z0-9_-]+)""\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Disconnect = new(LogPrefix + @"CL:\s*(?:Disconnecting from server|Server disconnected):",
        RegexOptions.CultureInvariant);
    private static readonly Regex ChatColors = new(@"[\x00-\x1f]", RegexOptions.CultureInvariant);

    private long? offset;
    private DateTime created;
    private string currentMap = "";
    private string? logMap;
    private KzMode? mode;

    internal KzMode? Read(string map)
    {
        if (currentMap != map && logMap != map)
        {
            logMap = null;
            mode = null;
        }
        currentMap = map;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var creation = File.GetCreationTimeUtc(path);
            long length = stream.Length;
            if (offset is null || length < offset || creation != created)
            {
                // Start at the live tail when attaching. A stored mode from a prior game is not current.
                offset = length;
                created = creation;
                logMap = null;
                return mode = null;
            }

            var start = offset.Value;
            if (length - start > MaxReadBytes)
            {
                start = length - MaxReadBytes;
                logMap = null;
                mode = null;
            }
            if (length > start) ReadMessages(stream, start, (int)(length - start));
            return logMap is null || logMap == map ? mode : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            offset = null;
            logMap = null;
            return mode = null;
        }
    }

    private void ReadMessages(FileStream stream, long start, int length)
    {
        stream.Position = start;
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        int lastLine = Array.LastIndexOf(bytes, (byte)'\n');
        if (lastLine < 0) return;
        int firstLine = start == offset ? 0 : Array.IndexOf(bytes, (byte)'\n') + 1;
        offset = start + lastLine + 1;

        foreach (var line in Encoding.UTF8.GetString(bytes, firstLine, lastLine + 1 - firstLine).Split('\n'))
        {
            string plain = ChatColors.Replace(line, "");
            var map = ConsoleMap.Match(plain);
            if (map.Success)
            {
                string name = map.Groups["map"].Value;
                logMap = name[(name.LastIndexOf('/') + 1)..].ToLowerInvariant();
                mode = null;
            }
            else if (Disconnect.IsMatch(plain))
            {
                logMap = "";
                mode = null;
            }
            else if (ModeMessage.Match(plain) is { Success: true } match)
            {
                mode = match.Groups["mode"].Value.ToLowerInvariant() is "classic" or "ckz" ? KzMode.Ckz : KzMode.Vnl;
            }
        }
    }
}
