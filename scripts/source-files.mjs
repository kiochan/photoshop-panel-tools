import { lstat, readdir } from "node:fs/promises";
import path from "node:path";
import { root } from "./paths.mjs";

// Shared upload allowlist. Original references, old copies and output are never traversed.
export const sourceRoots = [".github", "src", "cep", "shared", "scripts", "screenshots", "docs"];
export const sourceDocuments = [
  ".gitignore",
  ".gitattributes",
  "AGENTS.md",
  "README.md",
  "README.zh-CN.md",
  "BUILDING.md",
  "COMPATIBILITY.md",
  "THIRD_PARTY.md",
  "LICENSE",
  "CONTRIBUTING.md",
  "SECURITY.md",
  "CHANGELOG.md",
  ".editorconfig",
  "global.json",
  "release.md",
  "PRIVACY.md",
  "package.json",
  "package-lock.json",
  "biome.json",
  "NuGet.Config",
];
export function forbidden(file) {
  return (
    /^cep\/helper(?:\/|$)/i.test(file) ||
    /(?:^|\/)(?:node_modules|bin|obj|dist|artifacts|\.cache|ref|analysis|helper-runtime|\.git|\.codex|\.agents)(?:\/|$)/i.test(
      file,
    ) ||
    /(?:^|\/)(?:\.env(?:\..*)?|\.npmrc|\.netrc|credentials[^/]*\.json|secrets[^/]*\.json|return\.json|settings\.local\.json)$/i.test(
      file,
    ) ||
    /\.(?:exe|dll|pdb|zip|zxp|psd|psb|pdf|pem|key|pfx|p12|jks|keystore|log|jsonl|dmp|tmp|bak)$/i.test(
      file,
    )
  );
}
export function allowed(file) {
  return (
    !forbidden(file) &&
    (sourceDocuments.includes(file) || sourceRoots.some((dir) => file.startsWith(`${dir}/`)))
  );
}
export async function sourceFiles() {
  const files = [];
  async function visit(relative) {
    if (forbidden(relative)) return;
    const absolute = path.join(root, relative);
    let stat;
    try {
      stat = await lstat(absolute);
    } catch (error) {
      if (error.code === "ENOENT") return;
      throw error;
    }
    if (stat.isSymbolicLink()) throw new Error(`Source symlinks are not allowed: ${relative}`);
    if (stat.isDirectory()) {
      for (const name of (await readdir(absolute)).sort()) await visit(`${relative}/${name}`);
    } else if (stat.isFile()) files.push(relative);
  }
  for (const entry of [...sourceDocuments, ...sourceRoots]) await visit(entry);
  return files.sort();
}
