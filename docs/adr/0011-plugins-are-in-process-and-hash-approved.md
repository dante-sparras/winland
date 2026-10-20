# Plugins are in-process .NET assemblies, approved by hash

A plugin is a .NET assembly loaded into Winland. It can register Dispatchers, config keywords and event handlers. It loads at startup from the config, and loads and unloads at runtime through `winlandctl plugin`, using a collectible `AssemblyLoadContext`. A plugin can also register a tiled layout algorithm.

.NET has no in-process sandbox, so a loaded plugin has the full rights of an elevated Winland. The security is in what may load, and in containing faults:

- The plugin directory is writable only by administrators. A plugin loads only when its SHA-256 hash is approved in the protected config. A changed file stops loading until it is approved again.
- Every call into a plugin is wrapped. An exception is caught, logged and reported, and a plugin that keeps failing is disabled. Calls have a time budget, so a hung plugin can't freeze the event loop.
- The plugin API has one version number, compared exactly with the version a plugin declares. A mismatch refuses to load.

Hyprland's plugins are native code with the same trust, loaded without hash approval or fault containment. Winland keeps the model and adds both.
