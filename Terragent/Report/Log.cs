using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Terragent.Report;

/// <summary>
/// A structured record of what the agent did, one JSON object per line.
/// </summary>
// Log rather than Logger, the name tModLoader gives every Mod for its own log4net
// writer: inside a Mod the inherited property wins, so ours would need spelling out in
// full at every call in that file.
//
// One JSON object per line, because the diagnoses worth having come from reading these
// back with a script (a route redrawn each tick, an aim off by a tile, a torch never
// placed), and none of that survives being flattened into a sentence.
internal static class Log
{
    private static StreamWriter? _writer;
    private static string _lastKey = "";
    private static int _repeats;
    private static DateTime _started;

    public static string Path { get; private set; } = "";

    public static void Open(string directory)
    {
        Close();
        try
        {
            Directory.CreateDirectory(directory);
            _started = DateTime.Now;
            Path = System.IO.Path.Combine(directory,
                $"agent-{_started:yyyyMMdd-HHmmss}.jsonl");
            _writer = new StreamWriter(Path, append: false) { AutoFlush = true };
        }
        catch (IOException)
        {
            // A journal that cannot open must not stop the game. Losing the log is
            // annoying; losing the session is worse.
            _writer = null;
        }
    }

    public static void Close()
    {
        Flush();
        _writer?.Dispose();
        _writer = null;
        _lastKey = "";
        _repeats = 0;
    }

    /// <summary>Record something, collapsing an immediate repeat into a count.</summary>
    public static void Event(string category, string message,
        params (string Key, object? Value)[] fields)
    {
        if (_writer is null)
        {
            return;
        }

        string key = category + "\0" + message;
        if (key == _lastKey)
        {
            _repeats++;
            return;
        }

        Flush();
        _lastKey = key;
        _repeats = 0;
        Write(category, message, 0, fields);
    }

    /// <summary>Record unconditionally, repeats and all. For periodic snapshots.</summary>
    public static void Sample(string category, string message,
        params (string Key, object? Value)[] fields)
    {
        Flush();
        _lastKey = "";
        Write(category, message, 0, fields);
    }

    private static void Flush()
    {
        if (_writer is null || _repeats <= 0 || _lastKey.Length == 0)
        {
            return;
        }

        string[] parts = _lastKey.Split('\0');
        int repeats = _repeats;
        _repeats = 0;
        Write(parts[0], parts[1], repeats);
    }

    private static void Write(string category, string message, int repeats,
        params (string Key, object? Value)[] fields)
    {
        if (_writer is null)
        {
            return;
        }

        StringBuilder line = new(256);
        line.Append("{\"t\":")
            .Append((DateTime.Now - _started).TotalSeconds.ToString("0.00",
                CultureInfo.InvariantCulture))
            .Append(",\"cat\":").Append(Quote(category))
            .Append(",\"msg\":").Append(Quote(message));

        if (repeats > 0)
        {
            line.Append(",\"repeats\":").Append(repeats + 1);
        }

        foreach ((string key, object? value) in fields)
        {
            line.Append(',').Append(Quote(key)).Append(':').Append(Render(value));
        }

        line.Append('}');
        try
        {
            _writer.WriteLine(line.ToString());
        }
        catch (IOException)
        {
            _writer = null;
        }
    }

    private static string Render(object? value) => value switch
    {
        null => "null",
        bool flag => flag ? "true" : "false",
        int number => number.ToString(CultureInfo.InvariantCulture),
        float number => number.ToString("0.###", CultureInfo.InvariantCulture),
        double number => number.ToString("0.###", CultureInfo.InvariantCulture),
        IEnumerable<string> items => "[" + string.Join(",", Quoted(items)) + "]",
        _ => Quote(value.ToString() ?? ""),
    };

    private static IEnumerable<string> Quoted(IEnumerable<string> items)
    {
        foreach (string item in items)
        {
            yield return Quote(item);
        }
    }

    private static string Quote(string text)
    {
        StringBuilder quoted = new(text.Length + 2);
        quoted.Append('"');
        foreach (char character in text)
        {
            switch (character)
            {
                case '"': quoted.Append("\\\""); break;
                case '\\': quoted.Append("\\\\"); break;
                case '\n': quoted.Append("\\n"); break;
                case '\r': quoted.Append("\\r"); break;
                case '\t': quoted.Append("\\t"); break;
                default:
                    if (character < ' ')
                    {
                        quoted.Append("\\u").Append(((int)character).ToString("x4"));
                    }
                    else
                    {
                        quoted.Append(character);
                    }

                    break;
            }
        }

        return quoted.Append('"').ToString();
    }
}
