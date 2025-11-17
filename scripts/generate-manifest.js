#!/usr/bin/env node
const fs = require("fs");
const YAML = require("yaml");
const crypto = require("crypto");

// Environment variables from workflow
const releaseTag = process.env.RELEASE_TAG;
const releaseUrl = process.env.RELEASE_URL; 
const releasePackagePath = process.env.RELEASE_PACKAGE_PATH; 
const releaseMd5Path = process.env.RELEASE_MD5; // optional MD5 file from Publish Plugin workflow

if (!releaseTag || !releaseUrl || !releasePackagePath) {
  console.error("RELEASE_TAG, RELEASE_URL, and RELEASE_PACKAGE_PATH must be provided");
  process.exit(1);
}

// Load build.yaml
const data = YAML.parse(fs.readFileSync("build.yaml", "utf8"));

// Determine checksum
let checksum;
if (releaseMd5Path && fs.existsSync(releaseMd5Path)) {
  checksum = fs.readFileSync(releaseMd5Path, "utf8").trim();
} else {
  const packageBuffer = fs.readFileSync(releasePackagePath);
  checksum = crypto.createHash("md5").update(packageBuffer).digest("hex");
}

// Timestamp in ISO 8601 UTC format
const timestamp = new Date().toISOString();

// Load existing manifest.json if it exists
let manifest = [];
const manifestPath = "manifest.json";

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
const versionData = {
  version: data.version,
  changelog: data.changelog || "",
  targetAbi: data.targetAbi || "",
  sourceUrl: releaseUrl,
  checksum: checksum,
  timestamp: timestamp
};

// Check if version already exists
const existingVersion = plugin.versions.find(v => v.version === data.version);
if (existingVersion) {
  // Update all fields
  Object.assign(existingVersion, versionData);
} else {
  // Prepend new version
  plugin.versions.unshift(versionData);
}

// Write updated manifest.json
fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 4));
console.log(`Updated manifest.json with version ${data.version}`);

