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

`name` is the plugin's `IPlugin.Name`. A file whose hash is not in the list does not load, whether it was never approved or the bytes changed. The assembly is not loaded until the hash matches. The plugin manager adds `file`, `repository` and `commit` to the entries it writes. The host ignores them.

An assembly loads only from inside the plugin directory. A copy anywhere else does not load, even when its hash is approved:

`Plugin assembly 'C:\Users\me\sample.dll' is outside the plugin directory 'C:\ProgramData\Winland\plugins'.`

The plugin directory defaults to `%ProgramData%\Winland\plugins`. `WINLAND_PLUGIN_DIR` overrides it. Any program the user runs can set the user's environment, so Winland follows `WINLAND_PLUGIN_DIR` and `WINLAND_PLUGIN_APPROVALS` only when the location they name passes the check below. Otherwise it logs why and loads from the default directory. Winland refuses to load, and reports why, when a non-administrator can write the directory or the approvals file. `SYSTEM`, `Administrators` and `TrustedInstaller` may own and write them. Any other owner, and any other principal with write access, blocks every load. That includes the current user, even when the user is an administrator: under UAC every program the user runs without elevation has the user's SID. Every folder above the directory, up to the drive, must also be owned by one of those three, and no one else may delete or rename what it holds or change its access list, since that would let a program move the protected directory away and put its own in its place. Making new files and folders there is allowed, as `C:\` and `%ProgramData%` let users do. Winland also refuses a plugin directory or approvals file that is, or is inside, a junction or another reparse point. It reads the approvals through the file handle it checked, so a file put in their place after the check does not count.

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

Every call into a plugin — the constructor, `Name` and `ApiVersion` when it loads, initialize, shutdown, a dispatcher, a keyword and an event — is wrapped. A constructor or getter that throws or runs past the budget refuses the load and says why. An exception is caught, logged and counted. Each plugin has one thread of its own that runs its calls in order. A call that runs longer than its budget is abandoned on that thread, logged and counted. A call queued behind one that hangs waits its own budget and fails too, so a plugin that stays hung is soon disabled. The budget defaults to 250 ms and applies to dispatcher and event calls; the host uses that same budget for the other calls. Three consecutive failures disable the plugin. Further calls do not enter it. A successful call resets the consecutive count. Unload still runs `Shutdown`, unregisters the plugin's dispatchers, keywords and event handlers, and unloads the collectible context.

## Plugin manager

`winlandctl` installs plugins from a git repository:

```
winlandctl plugin install <git-url> [revision]
winlandctl plugin update [--force]
winlandctl plugin remove <git-url|name|author/name>
winlandctl plugin enable <name|author/name>
winlandctl plugin disable <name|author/name>
winlandctl plugin reload [--force]
winlandctl plugin list --installed
```

`install`, `update` and `remove` do not use the pipe except to unload and reload plugins in the running Winland. They need `git` and the .NET SDK on `PATH`.

`install` clones the repository into the staging folder and checks out `revision` when one is given. It reads `winland-plugin.toml` at the repository root, builds each plugin with `dotnet publish` as the current user, and prints the repository, the commit hash and the SHA-256 of each built assembly. Then Windows asks for one UAC confirmation. On approval, an elevated `winlandctl plugin apply` copies each assembly to `<plugin directory>\<name>.dll`, hashes the copy, and writes its approval only when that hash equals the one printed. One mismatch installs nothing. The elevated step creates the default plugin directory when it is missing, and every time makes `Administrators` the owner of it and of `%ProgramData%\Winland` above it and gives both an access list that only `SYSTEM` and `Administrators` can write, because a non-administrator may have created either folder first. It sets that through a handle to the folder itself, so a junction swapped in meanwhile is refused instead of followed. It refuses a plugin directory that is, or is inside, a junction or another reparse point. It never creates or protects a plugin directory set by `WINLAND_PLUGIN_DIR` or `WINLAND_PLUGIN_APPROVALS`: an administrator must have set that one up, with its approvals file, and it must pass the same check as the host. `install` refuses a repository that is already installed, and a plugin name that is already approved from another repository or by hand.

`install` and `update` refuse to run from an elevated shell. Building runs the repository's code, and that must not run as an administrator. Nothing loads from the staging folder, and the elevated step writes nothing there: it sends what it did back over a named pipe that `winlandctl` opened for the current user only.

`update` clones each installed repository again. A repository whose commit has not changed is skipped unless `--force` is given. The rest are rebuilt and approved together, through one confirmation. A plugin that is loaded in the running Winland is unloaded and loaded again from the new file. When it does not load again, `update` says so and exits with code 1. A plugin the manifest no longer lists is removed, and unloaded from the running Winland.

`remove` takes a repository URL, `author/name` or a repository's name, as the pinned plugin manager does, or the name of one of its plugins, and removes every plugin from that repository: the files, the approvals, and the plugins loaded in the running Winland. A plugin approved by hand is not removed this way.

`enable` and `disable` take a plugin's name, or `author/name` with the repository's author. As in the pinned plugin manager, the author is the part of the URL before the repository's last path segment, and a repository's name is the manifest's `name`, or else the URL's last path segment. A plugin `install` adds starts disabled, and `update` keeps whether each plugin is enabled. After the change they do what `reload` does.

`reload` asks the running Winland which plugins are loaded. It unloads each one that is not enabled, whatever loaded it, a `plugin` line too, and loads each enabled one that is not loaded. With `--force` it unloads every loaded plugin and loads every enabled one again. Put `exec-once = winlandctl plugin reload` in the config to load the enabled plugins at startup, as with the pinned plugin manager's `reload`. The manager keeps which plugins are enabled in `state.json` in the staging folder. That file decides only what `reload` loads: a plugin still loads only when the approvals file lists its hash.

`list --installed` prints each repository with its commit and plugins, whether each is enabled, then the plugins approved by hand. `plugin list` without `--installed` still lists the plugins loaded in the running Winland.

The staging folder is `%LOCALAPPDATA%\Winland\plugin-staging`. `WINLAND_PLUGIN_STAGING` overrides it. The plugin directory and the approvals file come from the same defaults and variables the host uses.

### Manifest

`winland-plugin.toml` lists the plugins in a repository. Every table except `[repository]` is one plugin, and its name is the table name: letters, digits, `_` and `-`.

```toml
[repository]
name = "sample-repository"
authors = ["someone"]

[echo]
description = "Registers the echo dispatcher."
api_version = 1
project = "Echo/Echo.csproj"
assembly = "Echo.dll"
```

| key | required | meaning |
| --- | --- | --- |
| `api_version` | yes | The plugin API version the plugin is built for. A manifest with any plugin whose version is not `PluginApiVersion.Current` is refused before anything is built. |
| `project` | yes | The `.csproj` to publish, relative to the repository root. |
| `assembly` | no | The built file to install. The default is the project's file name with `.dll`. |
| `description`, `authors`, `author` | no | Shown to people. The manager does not use them. |

The manifest is a subset of TOML: tables, and keys set to a string, an integer, a boolean or an array of strings on one line. Anything else is an error. Unknown keys are ignored.

### Building against the API

The manager passes the path of the `Winland.PluginApi.dll` next to `winlandctl` as the MSBuild property `WinlandPluginApi`. A plugin project references it without copying it, because the host provides it:

```xml
<ItemGroup>
  <Reference Include="Winland.PluginApi">
    <HintPath>$(WinlandPluginApi)</HintPath>
    <Private>false</Private>
  </Reference>
</ItemGroup>
```

Only the one assembly is installed. A plugin that needs other assemblies at run time does not load them from the plugin directory.

A working sample repository is in [tests/Winland.Plugins.Tests/SampleRepository](../tests/Winland.Plugins.Tests/SampleRepository).
