---

{
"slug": "panel-tools",
"title": "Panel Tools",
"summary": "Bring a Photoshop plugin panel close to your pointer, with a per-panel choice of a live mirror or the real floating window.",
"status": "trial",
"compatibility": ["Windows x64", "Photoshop 2019 (20.x) and later: compatibility target, not runtime certification", "Native pen pressure, tilt and pure Wintab forwarding are not implemented in mirror mode"]
}
---

## New in 0.3.0 / 本次更新

- English and Simplified Chinese interfaces, including tray, crop and recovery messages. Save the native language preference and reopen the tool to apply it. The CEP launcher has its own immediate language selector.
- 中英双语界面及 README；裁剪、托盘、状态与恢复提示均已翻译。原生语言保存后重启生效，CEP 启动器即时切换。
- Fixed the crop outline window exception found during an isolated desktop demonstration. Synthetic controls accepted a mirrored click and slider drag; real-window summon/return restored the demo source's original bounds. **Photoshop and third-party plugins remain unverified.**
- 独立桌面演示中验证了裁剪、镜像点击和滑块、真实窗口移动与恢复；仍未启动 Photoshop，不能把演示结果视为插件兼容性验收。
- The repository READMEs include real English/Chinese configuration, crop and mirror screenshots using project-authored synthetic controls. No Photoshop or private plugin imagery is included.

## Panel recall features

- **Visual crop editor:** drag a rectangle on a live source-window preview, with numeric coordinates still available. Confirm the selection, then save the binding.
- **Per-slot mirror size memory:** manually resized mirrors keep their logical client size across sessions; new mirrors initially fit the source region to the screen.
- **Notification-area controls:** closing configuration keeps the helper running. Open configuration again from the tray or by launching the helper again; pause shortcuts, close mirrors or restore the real panel from the tray menu.
- **Slot status overview:** see missing or ambiguous windows, shortcut registration problems, mirror pauses and outstanding recovery. Failed operations also request a Windows notification.
- Shared native source compiles without warnings in .NET 8 and in the optional C# 5 / .NET Framework prototype. Both native builds now treat warnings as errors.

This remains a development trial. Selected paths passed isolated synthetic-window desktop checks; mixed-DPI and Photoshop/plugin compatibility still require runtime verification.

## Choose how each panel appears

- **Mirror and forward input:** keep the original panel in place and show its window image near your pointer. Acceptance of background input depends on the source plugin.
- **Move the real panel:** bring an already floating panel to your pointer and restore its original position afterwards. Docked panels need to be floated manually first.
- Set a different mode and shortcut for each of five slots. By default, press **Alt+1–5** once to summon and again to close or restore. Separate summon and return keys are optional.

## Install and get started

1. Download and extract the complete ZIP.
2. Run `helper/PanelMirror.exe`. The Windows x64 .NET runtime is bundled with this package.
3. Select a source window, choose its mode and shortcut, then save the binding. A delayed pointer picker can help identify a window or client area.

The `CSXS` folder is an optional Photoshop CEP launcher. Its loading behavior depends on your Photoshop/CEP configuration; installing it is not necessary to run the native tool.

## Verification and limits

This is a development trial. Static checks, compilation and packaging are covered by CI. **Photoshop runtime behavior has not been verified** on the supported target versions.

Mirrors do not automatically include external menus and dialogs. Hidden or minimized source windows are unsupported. Moving mode does not automatically undock or re-dock panels. If a real window cannot be restored, retain the recovery record and use the recovery action before rebinding it.

Global shortcuts may conflict with other applications; change them in the configuration window. The tool does not move the system pointer or inject into Photoshop to make mirror input work.

## Open source

Panel Tools is now maintained in its own [MIT-licensed public repository](https://github.com/kiochan/photoshop-panel-tools). Its builds and dev/prod publication are independent of the Bailey workspace. Existing shortcuts, extension IDs and local recovery records are preserved.
