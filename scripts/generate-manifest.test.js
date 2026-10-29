"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");
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
  replaceVersions: false,
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

test("the dev manifest lists only the latest build, even if an older one had a higher version", () => {
  let manifest = updateManifest([], build, release("2.10.11.1001", { replaceVersions: true }));
  manifest = updateManifest(manifest, build, release("2.0.0.3", { replaceVersions: true }));

  assert.deepEqual(manifest[0].versions.map(v => v.version), ["2.0.0.3"]);
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
