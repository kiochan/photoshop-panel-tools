# Verification / 验证记录

[English README](../README.md) · [中文说明](../README.zh-CN.md)

Updated: **2026-11-04**. This record distinguishes desktop demonstration from Photoshop/plugin compatibility. The previous 0.2.0 dev build and publication completed successfully in [CI](https://github.com/kiochan/photoshop-panel-tools/actions/runs/41529282982).

## Current evidence

| Check                         | Result and limits                                                                                                                                                                                                                                |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| English catalog coverage      | Automated checks cover all native translation call sites, nonempty translations and matching format placeholders.                                                                                                                                |
| CEP launcher logic            | Node sandbox tests cover language switching, saved/system preference, missing storage, absolute paths, old/unknown/wrong hosts, missing helper and rejected launch. Process creation is mocked: no CEP host or helper is started by these tests. |
| Native compilation            | .NET 8 build treats warnings as errors. Optional .NET Framework build explicitly uses `/langversion:5 /warnaserror+`. Neither compiler result proves Photoshop compatibility.                                                                    |
| English/Chinese configuration | Opened the real ManagerForm through the source-only demo harness; verified saved language after restart and a persisted source binding.                                                                                                          |
| Visual crop                   | Opened the real DWM preview, dragged a selection, confirmed X=15, Y=14, width=336, height=384, then saved the binding. Fixed the disabled-outline window exception found during this check.                                                      |
| Mirror interaction            | The synthetic source's click counter changed from 0 to 1 through the mirror; its slider changed from 36 to 87 px. This proves acceptance only by these WinForms demo controls.                                                                   |
| Toggle shortcut               | Alt+1 closed the active demo mirror.                                                                                                                                                                                                             |
| Real-window movement          | Explicit move mode relocated the synthetic source. A recovery journal existed during movement. After return, the captured source bounds matched the original position and size, and the journal was cleared.                                     |
| Close to tray and recall      | Closing configuration left the synthetic source running. A second invocation of the real helper delivered the wake request and showed the existing configuration again without creating a second source.                                         |
| Screenshot review             | Four real window captures contain only the tool and synthetic controls. No Photoshop, third-party plugin, private document, recovery file or desktop background is included.                                                                     |

The resource loader was also exercised by desktop startup. The English catalog is explicitly embedded with `WithCulture=false`; otherwise MSBuild infers a satellite culture from its filename and the startup resource lookup fails.

The headless native self-check also passed: all **228** embedded English lookups and .NET format templates, Chinese language switching, loading an old configuration without language/size fields, language/size save-and-load, and rejection of an unsupported language. It uses a fresh isolated settings directory and does not create forms or install hooks.

## Still pending / 仍待验证

- Photoshop 2019 (20.x), 2020 (21.x), 2025 (26.x), other target versions and individual third-party panels.
- Docked Photoshop region selection, changing workspace layouts, modal dialogs and separate plugin menus.
- Native pressure, tilt, eraser and Wintab forwarding: **not implemented** in mirror mode.
- Multiple Photoshop instances, mismatched privilege levels, mixed DPI, negative monitor coordinates and unplugged screens.
- Mirror manual-size persistence across mixed DPI. A desktop resize attempt did not establish a result, so it is not reported as passed.
- Abrupt helper termination, failed restoration and recovery after restarting Photoshop; pending host validation includes the conservative main/document-window exclusions.
- Actual CEP 9 loading and launching inside Photoshop. Node tests do not establish CEP runtime compatibility.

**Assessment:** the checked build is suitable for controlled trial against a disposable panel. It is not certified for unattended or production Photoshop workflows. Keep the source visible, test harmless controls first, and retain unresolved recovery records.

**结论：** 已检查的构建适合使用可丢弃的面板进行受控试用，尚不能宣称通过 Photoshop 日常生产环境验收。先测试无害控件，保留未解决的恢复记录。

## Reproduce the independent demo

The source-only harness uses the actual configuration, mirror, crop and recovery classes. It launches a separate synthetic WinForms source process, restricts the source list to that process, and places settings/recovery under its own output directory's `profile/`. It never opens Photoshop or reads/writes the normal user configuration. It is not included in the product ZIP.

```powershell
dotnet publish src/PanelTools.Demo/PanelTools.Demo.csproj -c Release -r win-x64 --self-contained true -o .cache/demo-runtime
```

Building does not run the harness. Launch `.cache/demo-runtime/PanelTools.Demo.exe` only when desktop testing is authorized. Use the configuration window's explicit Exit action afterwards; closing it alone keeps it in the tray. Demo profile files remain ignored build output.
