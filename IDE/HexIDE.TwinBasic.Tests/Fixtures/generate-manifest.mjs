import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { pathToFileURL } from 'node:url';
import { resolve } from 'node:path';
// Usage: node generate-manifest.mjs <pinned-impexp.mjs> <fixture>
const { parseProject } = await import(pathToFileURL(resolve(process.argv[2])).href);
const fixture = process.argv[3];
const entries = [];
function walk(entry, parent) {
  const path = parent === null ? '' : parent ? `${parent}/${entry.name}` : entry.name;
  const {name, kind, revision, flags, category} = entry;
  const row = { path, name, kind, revision, flags, category };
  if (kind === 'file') {
    row.length = entry.content.length;
    row.sha256 = createHash('sha256').update(entry.content).digest('hex');
    row.revisions = entry.revisions;
  }
  entries.push(row);
  for (const child of entry.children ?? []) walk(child, path);
}
walk(parseProject(readFileSync(fixture)), null);
writeFileSync(fixture + '.json', JSON.stringify(entries, null, 2) + '\n');
