# Project instructions

- This standalone repository is MIT-licensed Photoshop Panel Tools. Do not import Bailey or other private plugin source, private Git history, chat material or supplied reference files.
- Target Photoshop 2019 (20.x) and later, prioritizing 2019, 2020 and 2025. Keep the CEP launcher ES5 / CEP 9 compatible. Distinguish Photoshop, Windows, CEP and .NET compatibility.
- Each slot explicitly chooses mirroring/background message forwarding or moving a bound real floating panel. Default Alt+1–5 toggles summon/close or return; preserve customizable and separate return keys.
- Mirroring keeps the source and real pointer in place. Do not use SendInput, process injection, pointer movement or silent moving-mode fallback. Posted messages do not prove plugin acceptance; native pen pressure and Wintab forwarding are not implemented.
- Moving mode must persist the original position and verified identity before movement, preserve unresolved recovery records and only clear them after confirmed restoration. Never move the Photoshop main/document window or automatically undock panels.
- Keep src/PanelMirror (.NET 8 WinForms), src/PanelRecall (shared recovery plus optional .NET Framework prototype), shared/windows-native (C# 5 compatible helpers) and cep (ES5 launcher) independently maintainable. Preserve extension IDs, settings keys and protocols.
- npm ci installs locked tooling; npm run privacy:check and npm run check precede npm run package. Use only fresh successful build stages; failures must return nonzero. Verify absolute cleanup targets remain inside managed output directories.
- Do not launch Photoshop, extensions or native helpers, install extensions or perform host runtime tests under the current authorization. Static checks and compilation are allowed. Never describe compilation as Photoshop validation.
- dev/prod CI publishes only panel-tools to matching latest-only website channels, preserving Bailey and the other channel. No client automatic update installer is implemented. Keep secrets out of PR/fork jobs and do not use pull_request_target.
- Root package.json owns the version. Public release notes are release.md and optional reviewed screenshots/. Preserve MIT and third-party runtime notices in packages.
- Review tracked files and the complete staged diff before pushing. Ignore reference materials, user data, recovery records, credentials, binaries, archives, node_modules, bin/obj, dist, artifacts and .cache. Keep source attribution accurate.
- Do not push or deploy without user authorization; the current task authorizes initial standalone publication and CI/website setup.
