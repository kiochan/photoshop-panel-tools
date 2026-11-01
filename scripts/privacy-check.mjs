import { spawnSync } from "node:child_process";
import { readFile } from "node:fs/promises";
import path from "node:path";
import { root } from "./paths.mjs";
import { allowed, sourceFiles } from "./source-files.mjs";

const files = await sourceFiles();
const failures = [];
// Review tracked names as well: adding an ignore rule never untracks a secret.
const git = spawnSync("git", ["ls-files", "-z"], {
  cwd: root,
  encoding: "utf8",
  windowsHide: true,
});
if (git.status === 0) {
  for (const file of git.stdout.split("\0").filter(Boolean)) {
    if (!allowed(file)) failures.push(`${file}: outside the reviewed source allowlist`);
  }
}
const rules = [
  ["private key", /-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----/],
  ["GitHub token", /\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})\b/],
  ["cloud access key", /\bAKIA[A-Z0-9]{16}\b/],
  ["personal home path", /[A-Z]:[\\/]+Users[\\/]+[^\\/\s"'<>]+/i],
  [
    "embedded credential",
    /\b(?:password|api[_-]?key|access[_-]?token|client[_-]?secret)\s*[:=]\s*["'][^"'\s]{12,}["']/i,
  ],
  ["credential in URL", /https?:\/\/[^\s/]+:[^\s/]+@/i],
];
for (const file of files) {
  if (/\.png$/i.test(file)) continue;
  const text = await readFile(path.join(root, file), "utf8");
  for (const [index, line] of text.split(/\r?\n/).entries()) {
    for (const [name, pattern] of rules)
      if (pattern.test(line)) failures.push(`${file}:${index + 1}: ${name}`);
  }
}
if (failures.length) {
  // Never print matching values: CI logs must not become another leak.
  console.error(`Privacy review failed:\n${failures.join("\n")}`);
  process.exitCode = 1;
} else
  console.log(
    `Privacy patterns passed for ${files.length} source files; manual review is still required. No matched values logged.`,
  );
