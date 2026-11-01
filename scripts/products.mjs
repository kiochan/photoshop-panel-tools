import { readFile } from "node:fs/promises";
import path from "node:path";
import { root } from "./paths.mjs";

const descriptors = {
  "panel-tools": {
    workspace: "@kiochan/photoshop-panel-tools",
    directory: ".",
    prefix: "panel-tools",
  },
};

export async function productInfo(id) {
  const descriptor = Object.hasOwn(descriptors, id) ? descriptors[id] : null;
  if (!descriptor) throw new Error("Select panel-tools.");
  const directory = path.join(root, descriptor.directory);
  const config = JSON.parse(await readFile(path.join(directory, "package.json"), "utf8"));
  if (config.name !== descriptor.workspace || !/^\d+\.\d+\.\d+$/.test(config.version))
    throw new Error("Invalid workspace package identity.");
  return {
    id,
    ...descriptor,
    directory,
    version: config.version,
    archive: `${descriptor.prefix}-${config.version}.zip`,
  };
}

export async function selectedProducts(args = process.argv.slice(2)) {
  if (args.length === 0) return Promise.all(Object.keys(descriptors).map(productInfo));
  if (args.length !== 2 || args[0] !== "--product")
    throw new Error("Usage: [--product panel-tools]");
  return [await productInfo(args[1])];
}
