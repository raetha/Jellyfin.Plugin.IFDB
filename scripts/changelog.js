#!/usr/bin/env node
// Reads release notes from CHANGELOG.md (Keep a Changelog format: "## [1.2.3] - date" sections).
//
//   node changelog.js notes <version> [CHANGELOG.md]
//       Prints that version's section (without its heading). Fails if there isn't one.
//   node changelog.js build-yaml <version> <build.yaml> [CHANGELOG.md]
//       Puts that section into build.yaml's changelog, so the packaged plugin carries it.
//   node changelog.js released <version> [CHANGELOG.md]
//       Prints that version's release date ("## [2.0.0] - 2026-10-30"). Fails if the section
//       is missing or not dated yet ("Unreleased"), so an undated version is never released.
//
// <version> may have three or four parts; "2.0.0.0" finds "## [2.0.0]".
"use strict";

const fs = require("fs");
const YAML = require("yaml");

function semver(version) {
  const parts = String(version).trim().split(".");
  if (parts.length < 3 || parts.length > 4 || !parts.every(p => /^\d+$/.test(p))) {
    throw new Error(`Not a version: "${version}"`);
  }
  return parts.slice(0, 3).join(".");
}

function extractNotes(changelog, version) {
  const wanted = semver(version);
  const lines = changelog.replace(/\r\n/g, "\n").split("\n");
  const start = lines.findIndex(l => new RegExp(`^## \\[${wanted.replace(/\./g, "\\.")}\\]`).test(l));
  if (start < 0) {
    throw new Error(`CHANGELOG.md has no "## [${wanted}]" section`);
  }

  let end = lines.findIndex((l, i) => i > start && /^## /.test(l));
  if (end < 0) end = lines.length;

  // Link references ("[2.0.0]: https://...") at the bottom aren't part of the notes.
  const body = lines.slice(start + 1, end).filter(l => !/^\[[^\]]+\]:\s/.test(l));
  const notes = body.join("\n").trim();
  if (!notes) {
    throw new Error(`CHANGELOG.md's "## [${wanted}]" section is empty`);
  }
  return notes;
}

function releaseDate(changelog, version) {
  const wanted = semver(version);
  const heading = changelog.replace(/\r\n/g, "\n").split("\n")
    .find(l => new RegExp(`^## \\[${wanted.replace(/\./g, "\\.")}\\]`).test(l));
  if (!heading) {
    throw new Error(`CHANGELOG.md has no "## [${wanted}]" section`);
  }
  const date = /^## \[[^\]]+\]\s*-\s*(\d{4}-\d{2}-\d{2})\s*$/.exec(heading);
  if (!date) {
    throw new Error(`CHANGELOG.md's "## [${wanted}]" heading has no release date (write "## [${wanted}] - YYYY-MM-DD")`);
  }
  return date[1];
}

function setBuildYamlChangelog(buildYaml, notes) {
  const doc = YAML.parseDocument(buildYaml);
  doc.set("changelog", notes);
  return doc.toString();
}

function main(args) {
  const [command, version, ...files] = args;
  if (command === "notes" && version) {
    const changelog = fs.readFileSync(files[0] || "CHANGELOG.md", "utf8");
    process.stdout.write(extractNotes(changelog, version) + "\n");
  } else if (command === "build-yaml" && version && files[0]) {
    const changelog = fs.readFileSync(files[1] || "CHANGELOG.md", "utf8");
    const updated = setBuildYamlChangelog(fs.readFileSync(files[0], "utf8"), extractNotes(changelog, version));
    fs.writeFileSync(files[0], updated);
  } else if (command === "released" && version) {
    const changelog = fs.readFileSync(files[0] || "CHANGELOG.md", "utf8");
    process.stdout.write(releaseDate(changelog, version) + "\n");
  } else {
    throw new Error("Usage: changelog.js notes <version> [CHANGELOG.md] | build-yaml <version> <build.yaml> [CHANGELOG.md] | released <version> [CHANGELOG.md]");
  }
}

module.exports = { semver, extractNotes, releaseDate, setBuildYamlChangelog, main };

if (require.main === module) {
  try {
    main(process.argv.slice(2));
  } catch (error) {
    console.error(error.message);
    process.exit(1);
  }
}
