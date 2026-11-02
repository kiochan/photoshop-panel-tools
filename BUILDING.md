# Building and publishing

Use Windows x64, Node.js 22+ and .NET SDK 8. `global.json` selects the .NET 8 feature band without raising the Photoshop 2019 minimum target. This repository is independent; no private source checkout or sibling folder is required.

```powershell
npm ci
npm run privacy:check
npm run check
npm run package
```

`npm run build:recall` optionally compiles the standalone .NET Framework prototype. CI builds the unified tool, not that optional executable. `npm run prepare:source` produces an allowlisted source snapshot; generated files are never committed. `npm run clean` removes only managed build output.

Both native builds treat compiler warnings as errors. The optional prototype explicitly compiles with `/langversion:5`; shared files use `NET8_0_OR_GREATER` guards for modern nullable-analysis attributes, keeping the C# 5 / .NET Framework build free of those attribute dependencies. To compile the prototype without packaging or launching it, run `powershell.exe -NoProfile -File scripts/build-recall.ps1`.

Failed compilation exits nonzero. Archives are created only from a new successful staging directory; a failed build cannot repack an earlier `dist` directory. Package metadata records `runtimeVerified: false`; compilation is not host validation. Keep LICENSE, THIRD_PARTY.md and runtime notices in distributed packages.

## Website publication

The `Panel Tools` workflow runs on push, pull request and manual dispatch. It checks privacy and source, compiles and retains the ZIP plus SHA256 as an Actions artifact. Publishing is a separate job, restricted to this upstream repository's dev/prod branches. Pull requests never publish; do not change this to `pull_request_target` or run untrusted code with secrets.

When bootstrapping a new repository, open its Actions tab and enable Actions if GitHub shows the repository-level activation banner. A workflow can appear active through the API and allow manual runs before automatic push events are enabled. Verify an actual push run after activation.

Configure the repository Actions Secret `RELEASE_SSH_KEY` with a dedicated restricted publication identity. Never put its private key in source, a package or a release note. The matching public key is configured by the website operator with a forced receiver command restricted to `kiochan/photoshop-panel-tools` and product `panel-tools`. Server identity is pinned in `.github/release-known_hosts`. `RELEASE_HOST` optionally overrides the pinned host name.

The publication job downloads only this build's artifact, prepares a validated public Markdown/package bundle, sends it to the receiver, then checks the website build SHA and ZIP download range. Only the matching channel/product catalogue entry changes; the other channel and Bailey remain intact. Successful publication removes older managed versions of this product/channel.

Stable entry points:

- https://kiochan.one/releases/panel-tools
- https://kiochan.one/downloads/panel-tools/prod/windows-x64
- https://kiochan.one/downloads/panel-tools/dev/windows-x64

Source is public on GitHub. Release descriptions and screenshots are read from the website's permanent read-only Docker mount, without rebuilding the website. There is no client auto-update installation feature.
