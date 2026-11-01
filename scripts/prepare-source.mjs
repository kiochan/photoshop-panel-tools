import { spawnSync } from "node:child_process";
import { createHash, randomUUID } from "node:crypto";
import { copyFile, mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { root } from "./paths.mjs";
import { sourceFiles } from "./source-files.mjs";

const stage = path.join(root, ".cache/build", `source-${randomUUID()}`, "photoshop-panel-tools");
const artifacts = path.join(root, "artifacts");
await mkdir(stage, { recursive: true });
await mkdir(artifacts, { recursive: true });
const inventory = [];
for (const file of await sourceFiles()) {
  const destination = path.join(stage, file);
  await mkdir(path.dirname(destination), { recursive: true });
  await copyFile(path.join(root, file), destination);
  const data = await readFile(destination);
  inventory.push({
    path: file,
    bytes: data.length,
    sha256: createHash("sha256").update(data).digest("hex"),
  });
}
const archive = path.join(artifacts, "photoshop-panel-tools-source.zip");
const result = spawnSync(
  "powershell.exe",
  [
    "-NoProfile",
    "-NonInteractive",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    path.join(root, "scripts/package-source.ps1"),
    "-Source",
    stage,
    "-Destination",
    archive,
  ],
  { stdio: "inherit", windowsHide: true },
);
if (result.error) throw result.error;
if (result.status !== 0) throw new Error(`Source snapshot failed (${result.status}).`);
const digest = createHash("sha256")
  .update(await readFile(archive))
  .digest("hex");
await writeFile(`${archive}.sha256`, `${digest}  ${path.basename(archive)}\n`);
await writeFile(`${archive}.files.json`, `${JSON.stringify({ files: inventory }, null, 2)}\n`);
console.log(
  `Prepared ${inventory.length} source files without uploading or initializing Git: ${archive}`,
);
