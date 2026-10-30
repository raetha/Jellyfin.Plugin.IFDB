"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { compareVersions, updateManifest } = require("./generate-manifest");

const build = {
  name: "IFDB",
  guid: "0f18c602-5d24-4f88-bb6e-9e59a2efb1cd",
  description: "Description\n",
  overview: "Overview",
  owner: "raetha",
  category: "Metadata",
  targetAbi: "12.0.0.0",
  changelog: "From build.yaml\n",
};

const release = (version, extra = {}) => ({
  version,
  sourceUrl: `https://example.com/ifdb_${version}.zip`,
  checksum: "0123456789abcdef0123456789ABCDEF",
  timestamp: "2026-10-29T00:00:00.000Z",
  replace: "",
  ...extra,
});

test("versions compare numerically, part by part", () => {
  assert.ok(compareVersions("2.0.0.10", "2.0.0.9") > 0);
  assert.ok(compareVersions("1.10.11.0", "2.0.0.0") < 0);
  assert.equal(compareVersions("2.0.0", "2.0.0.0"), 0);
});

test("creates the plugin entry in an empty manifest", () => {
  const manifest = updateManifest([], build, release("2.0.0.0"));

  assert.equal(manifest.length, 1);
  assert.equal(manifest[0].name, "IFDB");
  assert.equal(manifest[0].description, "Description");
  assert.deepEqual(manifest[0].versions, [{
    version: "2.0.0.0",
    changelog: "From build.yaml",
    targetAbi: "12.0.0.0",
    sourceUrl: "https://example.com/ifdb_2.0.0.0.zip",
    checksum: "0123456789abcdef0123456789abcdef",
    timestamp: "2026-10-29T00:00:00.000Z",
  }]);
});

test("keeps older versions newest first and replaces a rebuilt version", () => {
  let manifest = updateManifest([], build, release("1.10.11.0"));
  manifest = updateManifest(manifest, build, release("2.0.0.0"));
  manifest = updateManifest(manifest, build, release("1.11.0.0"));
  manifest = updateManifest(manifest, build, release("2.0.0.0", { changelog: "Rebuilt" }));

  const versions = manifest[0].versions;
  // 1.10.11 (the first release) is older than 1.11.0 but newer than a hypothetical 1.1.0.
  assert.deepEqual(versions.map(v => v.version), ["2.0.0.0", "1.11.0.0", "1.10.11.0"]);
  assert.ok(compareVersions("1.10.11.0", "1.1.0.0") > 0);
  assert.equal(versions[0].changelog, "Rebuilt");
});

test("the dev manifest lists only the latest build, even if an older one was numbered higher", () => {
  let manifest = updateManifest([], build, release("0.2.0.7", { replace: "all" }));
  manifest = updateManifest(manifest, build, release("2.0.0.9", { replace: "all" }));
  manifest = updateManifest(manifest, build, release("2.0.0.10", { replace: "all" }));

  assert.deepEqual(manifest[0].versions.map(v => v.version), ["2.0.0.10"]);
});

test("sets the catalog image when there is one", () => {
  const without = updateManifest([], build, release("2.0.0.0"));
  assert.equal(without[0].imageUrl, undefined);

  const withImage = updateManifest(without, build, release("2.0.0.1", { imageUrl: "https://example.com/image.png" }));
  assert.equal(withImage[0].imageUrl, "https://example.com/image.png");
});

test("command line writes the manifest", () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "manifest-"));
  const buildPath = path.join(dir, "build.yaml");
  const manifestPath = path.join(dir, "manifest.json");
  fs.writeFileSync(buildPath, 'name: "IFDB"\nguid: "g"\ntargetAbi: "12.0.0.0"\nchangelog: "Notes"\n');
  const { execFileSync } = require("child_process");
  const run = extra => execFileSync(process.execPath, [path.join(__dirname, "generate-manifest.js")], {
    env: { ...process.env, BUILD_YAML: buildPath, MANIFEST_PATH: manifestPath, SOURCE_URL: "https://x/a.zip", CHECKSUM: "0123456789abcdef0123456789abcdef  a.zip", ...extra },
    stdio: "pipe",
  });

  run({ VERSION: "2.0.0.0" });
  run({ VERSION: "1.11.0.0", CHANGELOG: "Older line" });

  const written = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
  assert.deepEqual(written[0].versions.map(v => [v.version, v.changelog]), [["2.0.0.0", "Notes"], ["1.11.0.0", "Older line"]]);
  assert.equal(written[0].versions[0].checksum, "0123456789abcdef0123456789abcdef");
  assert.throws(() => run({ VERSION: "" }), /Command failed/);
});

test("leaves other plugins in the manifest alone", () => {
  const other = { guid: "other", name: "Other", versions: [] };
  const manifest = updateManifest([other], build, release("2.0.0.0"));

  assert.equal(manifest.length, 2);
  assert.deepEqual(manifest[0], other);
});

test("rejects malformed versions and checksums", () => {
  assert.throws(() => updateManifest([], build, release("2.0.0")), /four numeric parts/);
  assert.throws(() => updateManifest([], build, release("2.0.0.0", { checksum: "nope" })), /MD5/);
});
