#!/usr/bin/env node
const fs = require("fs");
const YAML = require("yaml");
const crypto = require("crypto");

// Environment variables from workflow
const releaseTag = process.env.RELEASE_TAG;
const releaseUrl = process.env.RELEASE_URL; 
const releasePackagePath = process.env.RELEASE_PACKAGE_PATH; 
const releaseMd5Path = process.env.RELEASE_MD5; // optional MD5 file from Publish Plugin workflow
// Optional overrides, used for development builds from the dev branch
const manifestPath = process.env.MANIFEST_PATH || "manifest.json";
const versionOverride = process.env.VERSION_OVERRIDE;
const changelogOverride = process.env.CHANGELOG_OVERRIDE;
const keepVersions = parseInt(process.env.KEEP_VERSIONS || "0", 10); // 0 keeps every version

if (!releaseTag || !releaseUrl || !releasePackagePath) {
  console.error("RELEASE_TAG, RELEASE_URL, and RELEASE_PACKAGE_PATH must be provided");
  process.exit(1);
}

// Load build.yaml
const data = YAML.parse(fs.readFileSync("build.yaml", "utf8"));

// Determine checksum
let checksum;
if (releaseMd5Path && fs.existsSync(releaseMd5Path)) {
  // md5sum output is "<hash>  <file>"; the manifest wants just the hash
  checksum = fs.readFileSync(releaseMd5Path, "utf8").trim().split(/\s+/)[0];
} else if (fs.existsSync(releasePackagePath)) {
  const packageBuffer = fs.readFileSync(releasePackagePath);
  checksum = crypto.createHash("md5").update(packageBuffer).digest("hex");
} else {
  console.error(`Cannot find release package at ${releasePackagePath}`);
  process.exit(1);
}

// Timestamp in ISO 8601 UTC format
const timestamp = new Date().toISOString();

// Load existing manifest if it exists
let manifest = [];
if (fs.existsSync(manifestPath)) {
  manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
}

// Find or create plugin entry by GUID
let plugin = manifest.find(p => p.guid === data.guid);
if (!plugin) {
  plugin = {
    guid: data.guid,
    name: data.name,
    description: data.description || "",
    overview: data.overview || "",
    owner: data.owner || "",
    category: data.category || "",
    versions: []
  };
  manifest.push(plugin);
}

// Create new version object
const version = versionOverride || data.version;
const versionData = {
  version: version,
  changelog: changelogOverride || data.changelog || "",
  targetAbi: data.targetAbi || "",
  sourceUrl: releaseUrl,
  checksum: checksum,
  timestamp: timestamp
};

// Update existing version or prepend new
const existingVersion = plugin.versions.find(v => v.version === version);
if (existingVersion) {
  Object.assign(existingVersion, versionData);
} else {
  plugin.versions.unshift(versionData);
}

if (keepVersions > 0) {
  plugin.versions = plugin.versions.slice(0, keepVersions);
}

// Write updated manifest
fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 4));
console.log(`Updated ${manifestPath} with version ${version}`);
