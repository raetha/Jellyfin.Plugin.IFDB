#!/usr/bin/env node
// Adds a plugin build to a Jellyfin plugin repository manifest (manifest.json or manifest-dev.json).
//
// Environment:
//   BUILD_YAML      path to build.yaml (plugin name, guid, description, targetAbi, changelog)
//   MANIFEST_PATH   manifest to update; created if missing
//   VERSION         four-part plugin version, e.g. 2.0.0.0
//   SOURCE_URL      download URL of the plugin zip
//   CHECKSUM        MD5 of the zip (hex)
//   CHANGELOG       optional; defaults to build.yaml's changelog
//   REPLACE_VERSIONS optional; "1" makes this build the only version listed (for the rolling dev manifest)
//   TIMESTAMP       optional; ISO 8601 time, defaults to now
"use strict";

const fs = require("fs");
const YAML = require("yaml");

function compareVersions(a, b) {
  const pa = a.split(".").map(Number);
  const pb = b.split(".").map(Number);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const diff = (pa[i] || 0) - (pb[i] || 0);
    if (diff !== 0) return diff;
  }
  return 0;
}

function updateManifest(manifest, build, release) {
  if (!/^\d+\.\d+\.\d+\.\d+$/.test(release.version)) {
    throw new Error(`VERSION must have four numeric parts, got "${release.version}"`);
  }
  if (!/^[0-9a-f]{32}$/i.test(release.checksum)) {
    throw new Error(`CHECKSUM must be an MD5 hex digest, got "${release.checksum}"`);
  }

  let plugin = manifest.find(p => p.guid === build.guid);
  if (!plugin) {
    plugin = { guid: build.guid, versions: [] };
    manifest.push(plugin);
  }

  // Catalog details always reflect the newest build.
  Object.assign(plugin, {
    name: build.name,
    description: (build.description || "").trim(),
    overview: build.overview || "",
    owner: build.owner || "",
    category: build.category || "",
  });

  const entry = {
    version: release.version,
    changelog: (release.changelog || build.changelog || "").trim(),
    targetAbi: build.targetAbi || "",
    sourceUrl: release.sourceUrl,
    checksum: release.checksum.toLowerCase(),
    timestamp: release.timestamp,
  };

  plugin.versions = release.replaceVersions
    ? [entry]
    : plugin.versions.filter(v => v.version !== entry.version).concat(entry);
  plugin.versions.sort((a, b) => compareVersions(b.version, a.version));

  return manifest;
}

function main(env) {
  for (const name of ["BUILD_YAML", "MANIFEST_PATH", "VERSION", "SOURCE_URL", "CHECKSUM"]) {
    if (!env[name]) throw new Error(`${name} must be set`);
  }

  const build = YAML.parse(fs.readFileSync(env.BUILD_YAML, "utf8"));
  const manifest = fs.existsSync(env.MANIFEST_PATH)
    ? JSON.parse(fs.readFileSync(env.MANIFEST_PATH, "utf8"))
    : [];

  updateManifest(manifest, build, {
    version: env.VERSION,
    sourceUrl: env.SOURCE_URL,
    checksum: env.CHECKSUM.trim().split(/\s+/)[0],
    changelog: env.CHANGELOG,
    replaceVersions: env.REPLACE_VERSIONS === "1",
    timestamp: env.TIMESTAMP || new Date().toISOString(),
  });

  fs.writeFileSync(env.MANIFEST_PATH, JSON.stringify(manifest, null, 4) + "\n");
  console.log(`Added version ${env.VERSION} to ${env.MANIFEST_PATH}`);
}

module.exports = { compareVersions, updateManifest };

if (require.main === module) {
  try {
    main(process.env);
  } catch (error) {
    console.error(error.message);
    process.exit(1);
  }
}
