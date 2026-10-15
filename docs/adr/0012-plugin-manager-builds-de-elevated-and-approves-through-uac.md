# The plugin manager builds de-elevated and approves through UAC

`winlandctl plugin install <git-url>` clones the repository and builds it with `dotnet publish`, as the normal user, in a user-writable staging area. A repository has a `winland-plugin.toml` manifest listing one or more plugins, each with its name, API version and project path. A manifest that lists a plugin whose API version doesn't match the running Winland is refused before anything is built. The manager refuses to run from an elevated shell.

Nothing loads from the staging area. The manager shows the repository, the commit hash and the hash of each built assembly, and asks for one UAC confirmation. On approval, an elevated step copies the assemblies into the protected plugin directory and writes their hash approvals (ADR 0011). The elevated step hashes each copy and approves it only when it equals the hash the user was shown, so what loads is exactly what was approved. The host loads only from the plugin directory.

`update` and `remove` go through the same elevated step and the same one confirmation.

Building runs arbitrary code, through MSBuild targets, analyzers and source generators. It must not run elevated (ADR 0007), and the approval step is a user-presence check that a non-elevated program can't answer.
