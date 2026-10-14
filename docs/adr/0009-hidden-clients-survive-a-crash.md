# Hidden Clients survive a crash

Winland writes down which Clients it has hidden and which Workspace each belongs to. On a clean exit it shows every hidden Client. On startup it shows anything the previous run left hidden, and rejoins the Clients to their Workspaces.

Workspace switching hides Clients (ADR 0003). If Winland died without this, every Client on an inactive Workspace would stay hidden with no way to reach it.
