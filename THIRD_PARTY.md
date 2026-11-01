# Source and third-party notices

Photoshop Panel Tools was extracted from the Panel Tools portion of Kiochan's earlier private development project. This public repository starts with a reviewed source snapshot rather than publishing that project's private Git history. The project owner has authorized the included project source under the MIT license in LICENSE.

`shared/windows-native/CanvasClickHook.cs` and `ProcessIdentity.cs` originated as project-authored helpers shared with the separate Bailey workspace. This repository contains an independent source copy. Their historical use by Bailey does not include or license Bailey's upstream card-plugin code, artwork, CSS or other assets. None of those materials, third-party plugin binaries, supplied PDFs or chat records are included here.

- Build tooling uses Biome, locked in package-lock.json. Its upstream license notices remain applicable.
- The Windows self-contained distribution includes Microsoft's .NET runtime, whose license and third-party notices are supplied with its published files. Preserve those files when redistributing. The project MIT license does not replace third-party terms.
- Windows, Photoshop and CEP are provided by their respective owners; no Photoshop or CEP SDK binaries are bundled. Referencing Windows APIs does not imply Adobe endorsement or runtime certification.

If a source attribution or distribution issue is found, report it privately using SECURITY.md before including private reference material in an issue.
