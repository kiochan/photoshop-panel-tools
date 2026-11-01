import { lstat, realpath, rm } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const root = path.resolve(fileURLToPath(new URL("..", import.meta.url)));
export function inside(parent, target) {
  const relative = path.relative(parent, target);
  return (
    relative !== "" &&
    !relative.startsWith(`..${path.sep}`) &&
    relative !== ".." &&
    !path.isAbsolute(relative)
  );
}
export async function removeOutput(target) {
  const absolute = path.resolve(target);
  const allowed = ["dist", "artifacts", ".cache/build"].map((name) => path.join(root, name));
  if (!allowed.some((base) => absolute === base || inside(base, absolute)))
    throw new Error(`Refusing to remove ${absolute}`);
  try {
    const stat = await lstat(absolute);
    if (stat.isSymbolicLink() || !inside(await realpath(root), await realpath(absolute)))
      throw new Error(`Unsafe output path: ${absolute}`);
    await rm(absolute, { recursive: true, force: true });
  } catch (error) {
    if (error.code !== "ENOENT") throw error;
  }
}
