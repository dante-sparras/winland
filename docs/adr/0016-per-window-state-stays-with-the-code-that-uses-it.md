# Per-window state stays with the code that uses it

What Winland knows about one window lives in the module that uses it: its Props in `ClientProps`, its pin in `PinnedClients`, its owner in `Modal.cs`, its urgency in `Urgency.cs`, and so on. There is no single record per Client. Each store is forgotten through `WindowRegistry` when the window closes, and `WindowStateTests` walks every object Winland owns, including dictionary values and list elements, and fails when any of them still holds a closed window.

Most of these stores are read by one to three files. A single record would move each fact away from the only code that reads it, to save one forget call per store. The guard checks the rule that a closed window leaves nothing behind, which is the one reason to consider a single record.
