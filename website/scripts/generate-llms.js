#!/usr/bin/env node
/**
 * Generates /llms.txt and /llms-full.txt for AI/LLM consumption.
 *
 * llms.txt  — lightweight index: titles + descriptions + URLs
 * llms-full.txt — full concatenated content for context-window ingestion
 *
 * Both are written to static/ so Docusaurus serves them at the site root.
 * Run automatically via `prebuild` and can also be run standalone: node scripts/generate-llms.js
 */

const fs   = require('fs');
const path = require('path');

const DOCS_DIR    = path.join(__dirname, '..', 'docs');
const STATIC_DIR  = path.join(__dirname, '..', 'static');
const SITE_URL    = 'https://tasks.valkarn.com';

// ── Sidebar order (mirrors sidebars.js) ─────────────────────────────────────
const ORDER = [
  'installation',
  'quick-start',
  'migration-from-unitask',
  'concepts/struct-tasks',
  'concepts/pooling',
  'concepts/player-loop',
  'concepts/auto-cancel',
  'api/vlk-task',
  'api/vlk-task-t',
  'api/completion-source',
  'api/channels',
  'api/player-loop-timing',
  'api/settings',
  'advanced/burst-ecs',
  'advanced/job-bridge',
  'advanced/analyzers',
  'advanced/il2cpp',
  'advanced/testing',
  'license',
];

const SECTIONS = {
  '':          'Getting Started',
  'concepts/': 'Core Concepts',
  'api/':      'API Reference',
  'advanced/': 'Advanced',
};

// ── Helpers ──────────────────────────────────────────────────────────────────

function parseFrontmatter(content) {
  const match = content.match(/^---\n([\s\S]*?)\n---\n([\s\S]*)$/);
  if (!match) return { meta: {}, body: content };
  const meta = {};
  for (const line of match[1].split('\n')) {
    const [k, ...v] = line.split(':');
    if (k && v.length) meta[k.trim()] = v.join(':').trim();
  }
  return { meta, body: match[2] };
}

function firstParagraph(body) {
  const lines = body.split('\n');
  const paras = [];
  let inPara = false;
  for (const line of lines) {
    const trimmed = line.trim();
    if (!trimmed) { if (inPara) break; continue; }
    if (trimmed.startsWith('#') || trimmed.startsWith('```') || trimmed.startsWith('|') || trimmed.startsWith('-') || trimmed.startsWith('*')) {
      if (inPara) break;
      continue;
    }
    inPara = true;
    paras.push(trimmed);
  }
  return paras.join(' ').replace(/\*\*(.+?)\*\*/g, '$1').replace(/`(.+?)`/g, '$1').slice(0, 180);
}

function slugToUrl(slug) {
  return `${SITE_URL}/docs/${slug}`;
}

function sectionFor(slug) {
  for (const [prefix, name] of Object.entries(SECTIONS)) {
    if (prefix === '' && !slug.includes('/')) return name;
    if (prefix && slug.startsWith(prefix)) return name;
  }
  return 'Other';
}

// ── Collect docs in order ────────────────────────────────────────────────────

const docs = [];

for (const slug of ORDER) {
  const filePath = path.join(DOCS_DIR, slug + '.md');
  if (!fs.existsSync(filePath)) {
    console.warn(`  [warn] Missing: ${slug}.md`);
    continue;
  }
  const raw = fs.readFileSync(filePath, 'utf8');
  const { meta, body } = parseFrontmatter(raw);
  docs.push({
    slug,
    title:       meta.title || slug,
    description: firstParagraph(body),
    url:         slugToUrl(slug),
    section:     sectionFor(slug),
    body,
  });
}

// ── Build llms.txt ───────────────────────────────────────────────────────────

let llmsTxt = `# Valkarn Tasks

> Zero-allocation, struct-based async/await framework for Unity.
> Faster than UniTask. Source-generated lifecycle cancellation. Burst & ECS ready.

Valkarn Tasks is a Unity package providing a high-performance async/await implementation
that avoids heap allocations on the happy path, auto-cancels tasks when MonoBehaviours
are destroyed (via source generator), integrates with Unity Burst and ECS, and ships
17 Roslyn analyzer rules that catch async bugs at compile time.

License: Free for individuals and studios under $1M/year revenue.
Commercial license: ${SITE_URL}/licensing

`;

let currentSection = '';
for (const doc of docs) {
  if (doc.section !== currentSection) {
    currentSection = doc.section;
    llmsTxt += `## ${currentSection}\n\n`;
  }
  const desc = doc.description ? `: ${doc.description}` : '';
  llmsTxt += `- [${doc.title}](${doc.url})${desc}\n`;
}

llmsTxt += `
## Optional

- [License](${SITE_URL}/docs/license): Full license terms and commercial licensing information.
- [GitHub Repository](https://github.com/unapartidamas/valkarn-tasks): Source code, issues, and contributions.
- [Commercial Licensing](${SITE_URL}/licensing): Purchase an Annual Commercial License.
`;

// ── Build llms-full.txt ──────────────────────────────────────────────────────

let llmsFullTxt = `# Valkarn Tasks — Full Documentation

Source: ${SITE_URL}
GitHub: https://github.com/unapartidamas/valkarn-tasks
Generated: ${new Date().toISOString()}

---

`;

for (const doc of docs) {
  llmsFullTxt += `${'='.repeat(80)}\n`;
  llmsFullTxt += `# ${doc.title}\n`;
  llmsFullTxt += `URL: ${doc.url}\n`;
  llmsFullTxt += `${'='.repeat(80)}\n\n`;
  llmsFullTxt += doc.body.trim();
  llmsFullTxt += '\n\n';
}

// ── Write files ──────────────────────────────────────────────────────────────

fs.mkdirSync(STATIC_DIR, { recursive: true });

fs.writeFileSync(path.join(STATIC_DIR, 'llms.txt'),      llmsTxt,      'utf8');
fs.writeFileSync(path.join(STATIC_DIR, 'llms-full.txt'), llmsFullTxt,  'utf8');

const indexSize = (llmsTxt.length      / 1024).toFixed(1);
const fullSize  = (llmsFullTxt.length  / 1024).toFixed(1);

console.log(`✓ llms.txt      ${indexSize} KB  (${docs.length} pages)`);
console.log(`✓ llms-full.txt ${fullSize} KB`);
