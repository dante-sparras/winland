using System.Runtime.InteropServices;
using System.Text.Json;

namespace Winland.Lua;

// hl.notification. Before Winland runs, the notifications live here, so a create during the config exists at once.
// Once it runs they move to the core, under the same id when it is free.
internal sealed partial class LuaRun
{
    private readonly NotificationBook _pending = new();
    private readonly Dictionary<long, long> _notificationIds = [];
    private bool _handed;

    private void FlushNotifications()
    {
        if (_handed)
            return;

        _handed = true;
        if (_world is null)
            return;

        foreach (var snap in _pending.Snapshot())
        {
            if (snap.Gone)
            {
                _notificationIds[snap.Id] = 0;
                continue;
            }

            var actual = ReadId(_world.Command("notification " + RestoreJson(snap)));
            if (actual != snap.Id)
                _notificationIds[snap.Id] = actual;
        }
    }

    private string Ask(string json)
    {
        if (_handed && _world is not null)
            return _world.Command("notification " + json);

        return _pending.Execute(json, NotificationSpot.Fallback).Reply;
    }

    private long Mapped(long id) => _notificationIds.TryGetValue(id, out var mapped) ? mapped : id;

    private int MakeNotification(IntPtr state)
    {
        var id = _lua.IsNumber(1) ? _lua.ToInteger(1) : 0;
        var block = _lua.NewUserData(16);
        Marshal.WriteInt64(block, id);
        Marshal.WriteByte(block, 8, 0);
        if (_lua.IsTable(2))
        {
            _lua.PushCopy(2);
            _lua.SetMetaTable(-2);
        }

        return 1;
    }

    // Holds or releases this object's own pause. With no boolean it only reports the one it holds.
    private int NotificationHold(IntPtr state)
    {
        if (!TryBlock(1, out var block))
        {
            _lua.PushBoolean(false);
            return 1;
        }

        var paused = Marshal.ReadByte(block, 8) != 0;
        if (_lua.IsBoolean(2))
            Marshal.WriteByte(block, 8, (byte)(_lua.ToBoolean(2) ? 1 : 0));

        _lua.PushBoolean(paused);
        return 1;
    }

    // The finalizer. It only reads the userdata and releases a pause, and it pushes nothing: it runs as the Lua closes.
    private int NotificationRelease(IntPtr state)
    {
        if (!TryBlock(1, out var block) || Marshal.ReadByte(block, 8) == 0)
            return 0;

        Marshal.WriteByte(block, 8, 0);
        var id = Mapped(Marshal.ReadInt64(block));
        if (id == 0)
            return 0;

        try
        {
            _ = Ask(JsonSerializer.Serialize(new { op = "resume", id }));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }

        return 0;
    }

    private int NotificationOp(IntPtr state)
    {
        var op = _lua.IsString(1) ? _lua.ToString(1, false) ?? "" : "";
        long id = 0;
        if (op is not "create" and not "get")
        {
            if (!TryBlock(2, out var block))
            {
                _lua.PushNil();
                return 1;
            }

            id = Mapped(Marshal.ReadInt64(block));
            if (id == 0)
            {
                _lua.PushNil();
                return 1;
            }
        }

        string json;
        try
        {
            json = op switch
            {
                "create" => JsonSerializer.Serialize(new
                {
                    op,
                    text = _lua.IsString(2) ? _lua.ToString(2, false) ?? "" : "",
                    timeout = _lua.ToNumber(3),
                    icon = (int)_lua.ToInteger(4),
                    color = _lua.ToInteger(5),
                    font = _lua.ToNumber(6),
                }),
                "get" => """{"op":"get"}""",
                "query" or "dismiss" or "pause" or "resume" => JsonSerializer.Serialize(new { op, id }),
                "text" => JsonSerializer.Serialize(new { op, id, text = _lua.IsString(3) ? _lua.ToString(3, false) ?? "" : "" }),
                "timeout" => JsonSerializer.Serialize(new { op, id, timeout = _lua.ToNumber(3) }),
                "color" => JsonSerializer.Serialize(new { op, id, color = _lua.ToInteger(3) }),
                "icon" => JsonSerializer.Serialize(new { op, id, icon = (int)_lua.ToInteger(3) }),
                "font" => JsonSerializer.Serialize(new { op, id, font = _lua.ToNumber(3) }),
                _ => "",
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Warn("hl.notification: " + exception.Message, 2);
            _lua.PushNil();
            return 1;
        }

        if (json.Length == 0)
        {
            _lua.PushNil();
            return 1;
        }

        var reply = Ask(json);
        if (reply.StartsWith("error:", StringComparison.Ordinal))
        {
            Warn(reply, 2);
            _lua.PushNil();
            return 1;
        }

        if (reply == "null")
        {
            _lua.PushNil();
            return 1;
        }

        PushReply(reply);
        return 1;
    }

    private int NotificationColorValue(IntPtr state)
    {
        if (_lua.IsNil(1) || _lua.IsNone(1))
        {
            _lua.PushInteger(0);
            return 1;
        }

        if (_lua.IsNumber(1))
        {
            if (!NotificationColor.TryNumber(_lua.ToNumber(1), out var bits))
            {
                _lua.PushNil();
                return 1;
            }

            _lua.PushInteger(bits);
            return 1;
        }

        if (_lua.IsString(1) && NotificationColor.TryParse(_lua.ToString(1, false) ?? "", out var parsed))
        {
            _lua.PushInteger(parsed);
            return 1;
        }

        _lua.PushNil();
        return 1;
    }

    private bool TryBlock(int index, out IntPtr block)
    {
        block = IntPtr.Zero;
        if (!_lua.IsUserData(index))
            return false;

        block = _lua.ToUserData(index);
        return block != IntPtr.Zero;
    }

    private static string RestoreJson(NotificationRestore snap) =>
        JsonSerializer.Serialize(new
        {
            op = "restore",
            id = snap.Id,
            text = snap.Text,
            timeout = snap.Timeout,
            icon = snap.Icon,
            color = snap.Color,
            font = snap.Font,
            elapsed = snap.Elapsed,
            since = snap.Since,
            locks = snap.Locks,
        });

    private static long ReadId(string reply)
    {
        try
        {
            using var document = JsonDocument.Parse(reply);
            if (document.RootElement.TryGetProperty("id", out var id) && id.TryGetInt64(out var value))
                return value;
        }
        catch (JsonException)
        {
        }

        return 0;
    }
}
