import path from "node:path";
import { removeOutput, root } from "./paths.mjs";

for (const name of ["dist", "artifacts", ".cache/build"]) await removeOutput(path.join(root, name));
console.log("Removed build output and archives.");
