# Plugins

A plugin is a .NET assembly loaded into Winland. It can register dispatchers, config keywords and event handlers. Layouts from plugins are reserved and refused. The assembly is loaded in a collectible context, one per plugin, and dropped on unload.

The contract is the `Winland.PluginApi` project. A plugin references that project and exposes one public class with a parameterless constructor that implements `IPlugin`.

## API version

`PluginApiVersion.Current` is `1`. The host compares it exactly with `IPlugin.ApiVersion`. Any other value refuses the load:

`Plugin 'example' declares API version 2; Winland accepts 1.`

## What a plugin implements

```csharp
public interface IPlugin
{
    string Name { get; }
    int ApiVersion { get; }
    void Initialize(IPluginHost host);
    void Shutdown();
}
```

`Name` must match the `name` in the approvals file. `Initialize` registers the plugin's surface. `Shutdown` runs on unload.

## What the host accepts

```csharp
public interface IPluginHost
{
    void RegisterDispatcher(string name, PluginDispatcher handler);
    void RegisterKeyword(string name, PluginKeywordHandler handler);
    void Subscribe(PluginEventHandler handler);
    LayoutRegistration RegisterLayout(string name);
}
```

`RegisterDispatcher` adds the name to the dispatcher table. Binds and `winlandctl dispatch` call it through the same path as a built-in dispatcher. The handler receives the argument string and returns a `PluginResult`. Success with a value becomes the pipe reply. Success without a value replies `ok`. Failure replies `error:`.

`RegisterKeyword` handles one config keyword. The handler receives the value and an `IPluginWarnings` it can warn on. The keyword `plugin` itself is reserved. Keyword names are case-sensitive.

`Subscribe` receives stream events as `PluginEvent` values (`WindowOpened`, `WindowClosed`, `WindowFocused`, and the other records in the API).

`RegisterLayout` always returns `Supported: false` and the reason `Plugin layouts are not supported.`

## Approval

Plugins load only when the SHA-256 of the assembly file is listed in the approvals file. The default file is `%ProgramData%\Winland\plugins\approved.json`. `WINLAND_PLUGIN_APPROVALS` overrides that path. The file is a JSON array:

```json
[
  { "name": "sample", "sha256": "lowercase hex of the assembly" }
]
```

`name` is the plugin's `IPlugin.Name`. A file whose hash is not in the list does not load, whether it was never approved or the bytes changed. The assembly is not loaded until the hash matches.

The plugin directory defaults to `%ProgramData%\Winland\plugins`. `WINLAND_PLUGIN_DIR` overrides it. Winland refuses to load, and reports why, when a non-administrator can write the directory or the approvals file. `SYSTEM` and `Administrators` may write. An administrator who is the current user may write. Any other principal with write access blocks every load.

## Config and commands

The config keyword is `plugin`, the same spelling as the pinned line-based config:

```
plugin = C:\ProgramData\Winland\plugins\sample.dll
```

A path that is not rooted is relative to the config file's directory. Lines are read at startup and again on reload. After a plugin registers keywords, the config is read once more so those keywords apply.

`winlandctl` speaks to the pipe:

```
winlandctl plugin list
winlandctl plugin load <path>
winlandctl plugin unload <name>
```

`plugin list` prints a JSON array of `{ "name", "version", "state", "failures" }`. `version` is the plugin's API version. `state` is `loaded` or `disabled`. `failures` is the number of consecutive failed calls. `plugin load` and `plugin unload` reply `ok` or `error:`.

## Faults

Every call into a plugin — initialize, shutdown, a dispatcher, a keyword and an event — is wrapped. An exception is caught, logged and counted. A call that runs longer than its budget is abandoned on a worker, logged and counted. The budget defaults to 250 ms and applies to dispatcher and event calls; the host uses that same budget for the other calls. Three consecutive failures disable the plugin. Further calls do not enter it. A successful call resets the consecutive count. Unload still runs `Shutdown`, unregisters the plugin's dispatchers, keywords and event handlers, and unloads the collectible context.
