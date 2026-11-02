# Photoshop Panel Tools

[![CI](https://github.com/kiochan/photoshop-panel-tools/actions/workflows/ci.yml/badge.svg)](https://github.com/kiochan/photoshop-panel-tools/actions/workflows/ci.yml)

Bring a Photoshop plugin panel close to your pointer. Choose a mode independently for each of five slots:

- **Mirror and forward input:** keep the source panel in place and forward background mouse-compatible messages to its window tree.
- **Move a real floating panel:** temporarily move an explicitly bound floating window, then restore its recorded position and size.

Default shortcuts are **Alt+1–5**, toggling summon and close/restore with the same key. Custom combinations and separate return keys are supported.

Targets **Photoshop 2019 (20.x) and later on Windows x64**, with 2019, 2020 and 2025 as priority verification versions. This is an experimental trial: **Photoshop runtime compatibility has not been verified**. See [COMPATIBILITY.md](COMPATIBILITY.md).

## Download and use

The [official download and help page](https://kiochan.one/releases/panel-tools) offers the latest **prod** and **dev** builds, installation instructions and current limitations. Each channel retains only its latest package.

1. Extract the complete ZIP into a writable local folder.
2. Run `helper/PanelMirror.exe`; the .NET 8 runtime is bundled.
3. Choose a window, mode and shortcut for each slot, then save the bindings. In mirror mode, use the visual crop preview to drag a rectangle around the panel; numeric coordinates remain available. Float the source panel manually before choosing move mode.
4. Press the configured key to summon, then press it again to close the mirror or restore the real panel.

Each slot remembers a manually resized mirror's client size in DPI-independent units. New mirrors initially fit the source region to the screen. The five-slot status overview reports missing or ambiguous windows, shortcut registration problems, mirror pauses and outstanding real-panel recovery.

Closing the configuration window keeps the helper running in the Windows notification area. Double-click its tray icon, choose **打开配置** (open configuration), or launch the helper again to bring the configuration back. The tray menu can pause/resume shortcuts, close all mirrors, restore the real panel, or exit after attempting restoration. **仅退出，保留恢复记录** (exit and preserve recovery) remains available in the configuration window when restoration cannot complete. Windows notification settings control whether failure balloons are shown.

The `CSXS` folder provides an optional CEP launcher; native operation does not require installing the CEP extension. Installing unsigned CEP extensions depends on the host configuration. Keep the complete extracted package together.

Mirror input acceptance depends on each plugin. Native pen pressure, tilt, eraser and Wintab forwarding are not implemented. Menus and separate dialogs are not automatically mirrored. Hidden/minimized windows, layout changes, mixed DPI and ambiguous window identities need particular care. No pointer movement, `SendInput`, process injection or silent fallback to real-window movement is used to make mirror input work.

If a real panel cannot be restored, keep its local recovery record and use the recovery action before rebinding. Do not run another panel-moving tool against the same window at the same time.

## Build from source

Requirements: Windows x64, Node.js 22+, npm and .NET SDK 8. The optional standalone Panel Recall prototype also needs the Windows .NET Framework C# compiler.

```powershell
npm ci
npm run check
npm run package
```

Outputs: `dist/panel-tools/`, `artifacts/panel-tools-<version>.zip` and its `.sha256` file. `npm run build` compiles without creating a ZIP; `npm run build:recall` builds the optional prototype. These commands do not install or launch Photoshop, the CEP extension or either native executable.

The root `package.json` owns the product version. Existing extension IDs, local settings and recovery protocols are preserved during extraction from the original project.

## Repository and release process

- `src/PanelMirror/`: the .NET 8 WinForms application.
- `src/PanelRecall/`: the recovery engine and optional standalone .NET Framework prototype.
- `shared/windows-native/`: source-only canvas and process identity helpers.
- `cep/`: the ES5 / CEP 9 launcher.
- `scripts/`: checked builds, privacy review, packaging and website publication.
- `release.md` and optional `screenshots/`: reviewed public release notes and raster images.

Independent CI checks pull requests and pushes. Successful pushes to this repository's `dev` and `prod` branches publish only Panel Tools to the matching Kiochan website channel; no website rebuild is required. Forks and pull requests never receive publication credentials. Manual runs build by default and publish only when explicitly selected on dev/prod. Bailey has a separate repository and publication pipeline.

See [BUILDING.md](BUILDING.md), [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md) and [THIRD_PARTY.md](THIRD_PARTY.md). This repository uses the [MIT license](LICENSE); it does not include Photoshop, third-party plugins or their reference materials.
