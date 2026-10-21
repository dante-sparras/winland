# Unknown and malformed config lines warn and are skipped

A keyword that Winland does not know, or a malformed line, produces a warning and is skipped. The rest of the config still loads. A load with an unknown keyword or a malformed line shows one Windows notification with the count and the first of those, and a later load replaces it. A warning that a feature is not honored, has no Windows meaning, or is a window rule Winland does not apply is only logged. `winlandctl configerrors` and the event stream list every warning.

The config reloads when it is saved, so it is often read half-edited. Refusing the whole file over one typo would drop every bind until the typo is found, and a user with no binds cannot easily reach the editor to fix it.
