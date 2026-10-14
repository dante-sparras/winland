# The plugin manager builds de-elevated and approves through UAC

`winlandctl plugin install <git-url>` clones the repository and builds it with `dotnet publish`, as the normal user, in a user-writable staging area. A repository has a `winland-plugin.toml` manifest listing one or more plugins, each with its name, API version and project path. A plugin whose API version doesn't match the running Winland is refused before it is built.

Nothing loads from the staging area. The manager shows the repository, the commit hash and the hash of the built assembly, and asks for one UAC confirmation. On approval, an elevated step copies the assembly into the protected plugin directory and writes its hash approval (ADR 0011). The hash is computed after the copy, so what loads is exactly what was approved.

Building runs arbitrary code, through MSBuild targets, analyzers and source generators. It must not run elevated (ADR 0007), and the approval step is a user-presence check that a non-elevated program can't answer.
