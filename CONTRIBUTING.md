# Contributing

Use issues for reproducible bugs and focused feature requests. Include the tool version/channel, Windows and Photoshop versions, selected mode, source plugin, DPI/monitor layout and expected versus observed behavior. Redact artwork, window titles, user names, process paths and recovery data. Do not upload paid plugins or other third-party code.

Develop against `dev`; `prod` is the production release channel. Open a pull request against dev with a concise explanation and relevant validation. Install with `npm ci`, run `npm run privacy:check`, `npm run check` and `npm run package` on Windows. Compilation does not certify Photoshop behavior. Describe any manual testing accurately; current project automation must not launch Photoshop, install extensions or run native helpers.

Keep Photoshop 2019 (20.x) as the minimum compatibility target and CEP 9 / ES5 for the launcher. Preserve extension IDs, configuration keys and recovery identity checks unless there is an explicit migration. Move mode must remain an explicit user choice. Do not silently fall back from mirroring to moving a real panel, pointer manipulation or process injection.

Keep changes focused. Tests should cover meaningful behavior or security boundaries. Contributions are provided under this repository's MIT license; do not submit code or assets you are not authorized to distribute.
