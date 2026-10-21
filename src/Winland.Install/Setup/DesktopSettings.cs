using System.Text;
using System.Text.Json;

namespace Winland.Install;

/// <summary>
/// Snap windows and the precision touchpad swipe values the install script may turn off.
/// A swipe value that is absent is null. Setting null removes that value.
/// </summary>
public interface IWindowsDesktop
{
    bool WindowArranging { get; set; }

    int? ThreeFingerSlide { get; set; }

    int? FourFingerSlide { get; set; }
}

/// <summary>The file that records the values <see cref="DesktopSettings"/> changes.</summary>
public interface ISettingsText
{
    /// <summary>Null when the file is not there.</summary>
    string? Read();

    void Write(string text);

    void Delete();
}

/// <summary>Puts back settings recorded by the install script. <c>winland uninstall</c> calls this before it deletes anything.</summary>
public interface IDesktopRestore
{
    void Restore();
}

/// <summary>
/// Records the Windows settings the install script turns off, then changes them in one pass.
/// A later call keeps the first recorded values, so uninstall restores what the user had.
/// </summary>
public sealed class DesktopSettings : IDesktopRestore
{
    /// <summary>The registry value that means a precision touchpad swipe does nothing.</summary>
    public const int SwipeOff = 0;

    public const string WindowArrangingName = "windowArranging";

    public const string ThreeFingerName = "threeFingerSlide";

    public const string FourFingerName = "fourFingerSlide";

    private const string Corrupt =
        "The settings record in .winland could not be read. It was left in place. Fix that file and run winland uninstall again.";

    private readonly IWindowsDesktop windows;
    private readonly ISettingsText file;

    public DesktopSettings(IWindowsDesktop windows, ISettingsText file)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(file);
        this.windows = windows;
        this.file = file;
    }

    /// <summary>
    /// Reads every setting this call will change, records a value that is not already recorded, then writes them.
    /// <paramref name="touchpad"/> also turns off the three- and four-finger swipes.
    /// </summary>
    public void TurnOff(bool touchpad)
    {
        var arranging = windows.WindowArranging;
        int? three = null;
        int? four = null;
        if (touchpad)
        {
            three = windows.ThreeFingerSlide;
            four = windows.FourFingerSlide;
        }

        var record = Load();
        if (record.WindowArranging is null)
            record.WindowArranging = arranging;

        if (touchpad && !record.ThreeRecorded)
        {
            record.ThreeRecorded = true;
            record.ThreeFingerSlide = three;
        }

        if (touchpad && !record.FourRecorded)
        {
            record.FourRecorded = true;
            record.FourFingerSlide = four;
        }

        file.Write(Format(record));
        windows.WindowArranging = false;
        if (!touchpad)
            return;

        windows.ThreeFingerSlide = SwipeOff;
        windows.FourFingerSlide = SwipeOff;
    }

    public void Restore()
    {
        var text = file.Read();
        if (text is null)
            return;

        var record = Parse(text);
        if (record.WindowArranging is bool arranging)
            windows.WindowArranging = arranging;

        if (record.ThreeRecorded)
            windows.ThreeFingerSlide = record.ThreeFingerSlide;

        if (record.FourRecorded)
            windows.FourFingerSlide = record.FourFingerSlide;

        file.Delete();
    }

    private Record Load() => Parse(file.Read());

    private static Record Parse(string? text)
    {
        if (text is null)
            return new Record();

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException(Corrupt);

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException(Corrupt);

            var record = new Record();
            var root = document.RootElement;
            if (root.TryGetProperty(WindowArrangingName, out var arranging))
            {
                if (arranging.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                    throw new InvalidOperationException(Corrupt);

                record.WindowArranging = arranging.GetBoolean();
            }

            if (TrySlide(root, ThreeFingerName, out var three))
            {
                record.ThreeRecorded = true;
                record.ThreeFingerSlide = three;
            }

            if (TrySlide(root, FourFingerName, out var four))
            {
                record.FourRecorded = true;
                record.FourFingerSlide = four;
            }

            return record;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(Corrupt);
        }
    }

    private static bool TrySlide(JsonElement root, string name, out int? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var property))
            return false;

        if (property.ValueKind == JsonValueKind.Null)
            return true;

        if (!property.TryGetInt32(out var number))
            throw new InvalidOperationException(Corrupt);

        value = number;
        return true;
    }

    private static string Format(Record record)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            if (record.WindowArranging is bool arranging)
                writer.WriteBoolean(WindowArrangingName, arranging);

            WriteSlide(writer, ThreeFingerName, record.ThreeRecorded, record.ThreeFingerSlide);
            WriteSlide(writer, FourFingerName, record.FourRecorded, record.FourFingerSlide);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSlide(Utf8JsonWriter writer, string name, bool recorded, int? value)
    {
        if (!recorded)
            return;

        if (value is int number)
            writer.WriteNumber(name, number);
        else
            writer.WriteNull(name);
    }

    private sealed class Record
    {
        public bool? WindowArranging { get; set; }

        public bool ThreeRecorded { get; set; }

        public int? ThreeFingerSlide { get; set; }

        public bool FourRecorded { get; set; }

        public int? FourFingerSlide { get; set; }
    }
}
