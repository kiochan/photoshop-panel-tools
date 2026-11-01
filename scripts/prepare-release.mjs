import { spawnSync } from "node:child_process";
import { createHash, randomUUID } from "node:crypto";
import { cp, mkdir, readdir, readFile, stat, writeFile } from "node:fs/promises";
import path from "node:path";
import { root } from "./paths.mjs";
import { selectedProducts } from "./products.mjs";

const commit =
  process.env.GITHUB_SHA ||
  spawnSync("git", ["rev-parse", "HEAD"], {
    cwd: root,
    encoding: "utf8",
    windowsHide: true,
  }).stdout?.trim();
if (!/^[a-f0-9]{40}$/.test(commit || "")) throw new Error("A full build commit SHA is required.");
const products = await selectedProducts();
const stage = path.join(root, ".cache/build", `publication-${randomUUID()}`);
await mkdir(stage, { recursive: true });
const publishedAt = new Date().toISOString();
for (const product of products) {
  if (!/^\d+\.\d+\.\d+$/.test(product.version || "")) throw new Error("Invalid package version.");
  const archive = path.join(root, "artifacts", product.archive);
  const data = await readFile(archive);
  const sha256 = createHash("sha256").update(data).digest("hex");
  const expected = (await readFile(`${archive}.sha256`, "utf8")).trim().split(/\s+/)[0];
  if (sha256 !== expected) throw new Error(`Archive checksum mismatch: ${product.archive}`);
  const python = process.platform === "win32" ? "python" : "python3";
  const inspected = spawnSync(
    python,
    [
      "-c",
      "import json,sys,zipfile; z=zipfile.ZipFile(sys.argv[1]); names=[n for n in z.namelist() if n.count('/')==1 and n.endswith('/build-manifest.json')]; assert len(names)==1; m=json.loads(z.read(names[0])); print(json.dumps({'version':m['version'],'runtimeVerified':m['runtimeVerified']}))",
      archive,
    ],
    { encoding: "utf8", windowsHide: true },
  );
  if (inspected.error || inspected.status !== 0)
    throw new Error("Package manifest inspection failed.");
  const manifest = JSON.parse(inspected.stdout);
  if (manifest.version !== product.version || manifest.runtimeVerified !== false)
    throw new Error(
      "Package identity or verification declaration changed; review the publication notes.",
    );
  const template = await readFile(path.join(product.directory, "release.md"), "utf8");
  const match = /^---\r?\n([\s\S]*?)\r?\n---(?:\r?\n|$)([\s\S]*)$/.exec(template);
  if (!match) throw new Error("Release notes require JSON front matter.");
  const metadata = JSON.parse(match[1]);
  if (metadata.slug !== product.id) throw new Error("Release slug mismatch.");
  const destination = path.join(stage, "products", product.id);
  await mkdir(path.join(destination, "downloads"), { recursive: true });
  const document = {
    ...metadata,
    version: product.version,
    publishedAt,
    buildCommit: commit,
    downloads: [
      {
        id: "windows-x64",
        file: product.archive,
        label: "Download for Windows x64",
        bytes: data.length,
        sha256,
      },
    ],
  };
  await writeFile(
    path.join(destination, "release.md"),
    `---\n${JSON.stringify(document, null, 2)}\n---\n${match[2]}`,
  );
  await cp(archive, path.join(destination, "downloads", product.archive));
  const screenshots = path.join(product.directory, "screenshots");
  let images = [];
  try {
    images = await readdir(screenshots, { withFileTypes: true });
  } catch (error) {
    if (error.code !== "ENOENT") throw error;
  }
  if (images.length > 32) throw new Error("Too many publication screenshots.");
  for (const image of images) {
    const source = path.join(screenshots, image.name);
    if (
      !image.isFile() ||
      !/^[a-zA-Z0-9][a-zA-Z0-9._-]*\.(png|jpe?g|webp)$/.test(image.name) ||
      (await stat(source)).size > 8388608
    )
      throw new Error("Unexpected screenshot; publish only reviewed raster images.");
    await mkdir(path.join(destination, "screenshots"), { recursive: true });
    await cp(source, path.join(destination, "screenshots", image.name));
  }
}
await writeFile(
  path.join(stage, "bundle.json"),
  JSON.stringify({
    schema: 1,
    repository: "kiochan/photoshop-panel-tools",
    buildCommit: commit,
    products: products.map((product) => product.id),
  }),
);
const productLabel = products.length === 1 ? products[0].id : "all";
const archive = path.join(root, `artifacts/site-release-${productLabel}.tar.gz`);
const result = spawnSync("tar", ["-czf", archive, "-C", stage, "bundle.json", "products"], {
  stdio: "inherit",
  windowsHide: true,
});
if (result.error) throw result.error;
if (result.status !== 0) throw new Error("Website bundle packaging failed.");
console.log(
  `Prepared public release bundle for ${products.map((product) => product.id).join(", ")}: ${archive}`,
);
