"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("fs");
const os = require("os");
const path = require("path");
const YAML = require("yaml");
const { semver, extractNotes, setBuildYamlChangelog, main } = require("./changelog");

const changelog = `# Changelog

Intro text.

## [Unreleased]

## [2.0.0] - 2026-11-01

Requires Jellyfin 12.

### Fixed
- Searching works again.

## [1.10.11] - 2025-11-17

- Initial release.

[2.0.0]: https://github.com/example/compare/v1.10.11...v2.0.0
`;

test("versions reduce to their first three parts", () => {
  assert.equal(semver("2.0.0.0"), "2.0.0");
  assert.equal(semver("2.1.3"), "2.1.3");
  assert.throws(() => semver("2.0"), /Not a version/);
  assert.throws(() => semver("dev-2.0.0.5"), /Not a version/);
});

test("extracts a version's section without its heading", () => {
  assert.equal(extractNotes(changelog, "2.0.0.0"), "Requires Jellyfin 12.\n\n### Fixed\n- Searching works again.");
});

test("the last section stops before link references", () => {
  assert.equal(extractNotes(changelog, "1.10.11"), "- Initial release.");
});

test("doesn't match a version that merely starts the same", () => {
  assert.throws(() => extractNotes("## [2.0.0.1]\n- x\n", "2.0.0"), /no "## \[2\.0\.0\]" section/);
});

test("missing and empty sections are errors", () => {
  assert.throws(() => extractNotes(changelog, "3.0.0"), /no "## \[3\.0\.0\]" section/);
  assert.throws(() => extractNotes("## [1.0.0]\n\n## [0.9.0]\n- x\n", "1.0.0"), /is empty/);
});

test("handles Windows line endings", () => {
  assert.equal(extractNotes("## [1.0.0]\r\n- One\r\n", "1.0.0"), "- One");
});

test("replaces build.yaml's changelog and keeps everything else", () => {
  const updated = setBuildYamlChangelog('name: "IFDB"\nversion: "2.0.0.0"\nchangelog: >\n  old\n', "- New\n- Notes");
  const parsed = YAML.parse(updated);

  assert.equal(parsed.name, "IFDB");
  assert.equal(parsed.version, "2.0.0.0");
  assert.equal(parsed.changelog, "- New\n- Notes");
});

test("command line: notes and build-yaml", () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "changelog-"));
  const changelogPath = path.join(dir, "CHANGELOG.md");
  const buildPath = path.join(dir, "build.yaml");
  fs.writeFileSync(changelogPath, changelog);
  fs.writeFileSync(buildPath, 'version: "2.0.0.0"\nchangelog: "old"\n');

  const written = [];
  const original = process.stdout.write;
  process.stdout.write = chunk => { written.push(chunk); return true; };
  try {
    main(["notes", "2.0.0", changelogPath]);
  } finally {
    process.stdout.write = original;
  }
  assert.match(written.join(""), /Searching works again/);

  main(["build-yaml", "2.0.0.0", buildPath, changelogPath]);
  assert.match(YAML.parse(fs.readFileSync(buildPath, "utf8")).changelog, /Requires Jellyfin 12/);

  assert.throws(() => main(["bogus"]), /Usage/);
});
