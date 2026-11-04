import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import vm from "node:vm";

const root = path.resolve(import.meta.dirname, "../..");
const strings = JSON.parse(
  await readFile(path.join(root, "shared/windows-native/strings.en.json"), "utf8"),
);
const launcher = await readFile(path.join(root, "cep/panel.js"), "utf8");

test("native translations cover call sites and preserve format placeholders", async () => {
  const placeholders = (value) =>
    [...value.matchAll(/\{\d+(?::[^}]+)?\}/g)].map((m) => m[0]).sort();
  for (const [source, english] of Object.entries(strings)) {
    assert.ok(english.trim(), `Empty translation: ${source}`);
    assert.deepEqual(placeholders(english), placeholders(source), `Format mismatch: ${source}`);
    assert.equal(/[\u4e00-\u9fff]/u.test(english), false, `Untranslated value: ${source}`);
    assert.equal(english.includes("\\n"), false, `Literal newline escape: ${source}`);
  }
  let calls = 0;
  for (const folder of ["src/PanelMirror", "src/PanelRecall"]) {
    for (const file of await readdir(path.join(root, folder))) {
      if (!file.endsWith(".cs")) continue;
      const source = await readFile(path.join(root, folder, file), "utf8");
      for (const match of source.matchAll(/UiText\.(?:T|F)\("((?:\\.|[^"\\])*)"/g)) {
        const key = JSON.parse(`"${match[1]}"`);
        assert.ok(Object.hasOwn(strings, key), `Missing translation in ${file}: ${key}`);
        calls++;
      }
    }
  }
  assert.ok(calls > 200, "Expected coverage of the complete native interface");
  const project = await readFile(path.join(root, "src/PanelMirror/PanelMirror.csproj"), "utf8");
  assert.match(
    project,
    /strings\.en\.json[^>]+WithCulture="false"/,
    "Do not infer a satellite culture for the embedded catalog",
  );
});

function host({
  locale = "en-US",
  saved,
  version = "26.0",
  appId = "PHXS",
  extension = "file:///C:/Panel%20Tools/",
  storage = true,
  present = true,
  result = 0,
} = {}) {
  const elements = {};
  const launches = [];
  for (const id of [
    "launch",
    "status",
    "host",
    "language",
    "description",
    "compatibility",
    "shortcuts",
  ]) {
    elements[id] = {
      textContent: "",
      value: "",
      listeners: {},
      addEventListener(name, callback) {
        this.listeners[name] = callback;
      },
    };
  }
  const document = { documentElement: {}, getElementById: (id) => elements[id] };
  const window = {
    localStorage: {
      getItem() {
        if (!storage) throw new Error("Unavailable");
        return saved;
      },
      setItem() {
        if (!storage) throw new Error("Unavailable");
      },
    },
    __adobe_cep__: {
      getHostEnvironment: () => JSON.stringify({ appId, appVersion: version }),
      getSystemPath: () => extension,
      fs: { stat: () => ({ err: present ? 0 : 3 }) },
      process: {
        createProcess: (file) => {
          launches.push(file);
          return { err: result };
        },
      },
    },
  };
  vm.runInNewContext(launcher, { window, document, navigator: { language: locale } });
  return { elements, document, launches };
}

test("CEP chooses locale and switches languages without starting a process", () => {
  const h = host({ locale: "zh-CN" });
  assert.equal(h.document.documentElement.lang, "zh-CN");
  assert.equal(h.launches.length, 0);
  h.elements.language.value = "en";
  h.elements.language.listeners.change();
  assert.equal(h.elements.launch.textContent, "Open Panel Tools");
  assert.match(h.elements.host.textContent, /26.0/);
  assert.equal(h.launches.length, 0);
});

test("CEP saved preference overrides system locale; unavailable storage is optional", () => {
  assert.equal(host({ locale: "zh-CN", saved: "en" }).document.documentElement.lang, "en");
  assert.equal(host({ storage: false }).document.documentElement.lang, "en");
});

test("CEP launches only the packaged helper and decodes an absolute file URL", () => {
  const h = host();
  h.elements.launch.listeners.click();
  assert.deepEqual(h.launches, ["C:\\Panel Tools\\helper\\PanelMirror.exe"]);
  assert.match(h.elements.status.textContent, /Launch\/recall requested/);
});

test("CEP refuses a wrong host, an old/unknown version and a relative path", () => {
  for (const config of [
    { appId: "ILST" },
    { version: "19.1" },
    { version: "unknown" },
    { extension: "relative/path" },
  ]) {
    const h = host(config);
    h.elements.launch.listeners.click();
    assert.equal(h.launches.length, 0);
  }
});

test("CEP reports a missing helper and a rejected launch", () => {
  const missing = host({ present: false });
  missing.elements.launch.listeners.click();
  assert.equal(missing.launches.length, 0);
  assert.match(missing.elements.status.textContent, /complete ZIP/);
  const rejected = host({ result: 5 });
  rejected.elements.launch.listeners.click();
  assert.match(rejected.elements.status.textContent, /CEP error: 5/);
});

test("bilingual documentation links and reviewed screenshot formats are valid", async () => {
  for (const name of [
    "README.md",
    "README.zh-CN.md",
    "docs/VERIFICATION.md",
    "docs/SCREENSHOTS.md",
  ]) {
    const file = path.join(root, name);
    const content = await readFile(file, "utf8");
    for (const match of content.matchAll(/\]\(([^)]+)\)/g)) {
      if (match[1].includes("://") || match[1].startsWith("#")) continue;
      await readFile(path.resolve(path.dirname(file), match[1].split("#")[0]));
    }
  }
  const images = await readdir(path.join(root, "screenshots"));
  assert.equal(images.length, 4);
  for (const name of images) {
    assert.match(name, /^[a-zA-Z0-9][a-zA-Z0-9._-]*\.jpg$/);
    const data = await readFile(path.join(root, "screenshots", name));
    assert.equal(
      data.subarray(0, 3).toString("hex"),
      "ffd8ff",
      "Keep the capture's actual JPEG format",
    );
    assert.ok(data.length < 8388608);
  }
});
