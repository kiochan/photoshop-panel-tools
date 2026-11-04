# Changelog

## 0.3.0

- English and Simplified Chinese native interfaces, status/recovery messages and CEP launcher; native language selection persists across restarts.
- Expanded bilingual READMEs, reviewed real demo screenshots and a verification record separating synthetic controls from Photoshop compatibility.
- Fix the crop outline's disabled-window display exception and preserve the save reminder after crop confirmation.
- Embed the translation catalog in the main assembly instead of inferring a satellite culture.
- Translation and sandboxed launcher regression checks run with `npm run check`.
- Source-only isolated demo harness exercises the real product forms without Photoshop or normal user configuration.

## 0.2.0

- Resolve the 24 shared-source nullable warnings while retaining C# 5 compatibility; both native builds now treat warnings as errors.
- Visual DWM crop preview with drag selection and client-area validation before saving.
- Per-slot mirror client-size memory in logical pixels and source-proportional first-use sizing.
- Notification-area controls, close-to-tray behavior and repeated-launch configuration recall.
- Five-slot status overview and operation-failure notifications; restoration safeguards remain in place.
- Static/compile verification only; Photoshop and desktop runtime behavior remain unverified.

## 0.1.0

- Initial standalone MIT-licensed Panel Tools source snapshot.
- Per-slot mirror/input forwarding or real floating-panel movement, with configurable toggle or separate return shortcuts.
- Independent Windows CI, checksummed trial packages and separate dev/prod latest downloads on the Kiochan website.
- Photoshop 2019+ remains the compatibility target. No Photoshop runtime validation has been performed.
