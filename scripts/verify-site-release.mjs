import { selectedProducts } from "./products.mjs";

const base = new URL(process.env.RELEASE_SITE_URL || "https://kiochan.one");
const channel = process.env.RELEASE_CHANNEL;
const commit = process.env.GITHUB_SHA;
if (
  base.protocol !== "https:" ||
  !["prod", "dev"].includes(channel) ||
  !/^[a-f0-9]{40}$/.test(commit || "")
)
  throw new Error("Invalid publication verification settings.");
for (const { id: slug } of await selectedProducts()) {
  let passed = false;
  for (let attempt = 0; attempt < 5; attempt++) {
    try {
      const response = await fetch(new URL(`/api/releases/${slug}/${channel}`, base), {
        signal: AbortSignal.timeout(15000),
        cache: "no-store",
      });
      if (!response.ok) throw new Error("Release metadata is unavailable.");
      const release = await response.json();
      if (
        release.buildCommit !== commit ||
        release.channel !== channel ||
        release.downloads.length !== 1
      )
        throw new Error("Published build identity does not match.");
      const file = release.downloads[0];
      if (
        !file.url.startsWith(`/downloads/${slug}/${channel}/`) ||
        !/^[a-f0-9]{64}$/.test(file.sha256)
      )
        throw new Error("Unexpected download metadata.");
      const download = await fetch(new URL(file.url, base), {
        headers: { Range: "bytes=0-3", "If-Range": `"${file.sha256}"` },
        signal: AbortSignal.timeout(15000),
        cache: "no-store",
      });
      if (download.status !== 206 || download.headers.get("etag") !== `"${file.sha256}"`)
        throw new Error("Latest package range check failed.");
      const magic = new Uint8Array(await download.arrayBuffer());
      if (magic[0] !== 80 || magic[1] !== 75) throw new Error("Published download is not a ZIP.");
      passed = true;
      break;
    } catch {
      if (attempt === 4) throw new Error(`Website verification failed for ${slug}/${channel}.`);
      await new Promise((resolve) => setTimeout(resolve, 2000));
    }
  }
  if (passed) console.log(`Verified ${slug}/${channel}: current commit and fixed download URL.`);
}
