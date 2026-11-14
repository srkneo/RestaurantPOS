// Adds or updates texts in the English and Gujarati files from a patch file:
//   node scripts/i18n-add.mjs scripts/i18n/<patch>.json
// Patch format: { "key": ["English text", "Gujarati text"], ... }
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', 'ClientApp', 'ClientApp', 'src', 'i18n');
const read = (file) => JSON.parse(readFileSync(file, 'utf8').replace(/^﻿/, ''));
const patch = read(process.argv[2]);

['en', 'gu'].forEach((lang, index) => {
  const file = join(root, `${lang}.json`);
  const texts = read(file);
  for (const [key, values] of Object.entries(patch)) {
    if (!Array.isArray(values) || values.length !== 2) throw new Error(`Patch entry ${key} needs [English, Gujarati]`);
    texts[key] = values[index];
  }
  writeFileSync(file, JSON.stringify(texts, null, 2) + '\n', 'utf8');
});
console.log(`Added or updated ${Object.keys(patch).length} texts`);
