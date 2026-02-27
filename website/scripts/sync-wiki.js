const fs = require('fs');
const path = require('path');

const WIKI_DIR = path.resolve(__dirname, '../../wiki');
const DOCS_DIR = path.resolve(__dirname, '../docs');

// Files to skip (wiki-specific, not doc pages)
const SKIP_FILES = ['_Sidebar.md', '_Footer.md'];

// Known wiki page names for link transformation
const KNOWN_PAGES = [
  'Home', 'Installation', 'Quick-Start', 'Model-Setup',
  'Commands-and-Shortcuts', 'Tool-Reference', 'Configuration',
  'Sessions-and-Context', 'Project-Instructions', 'Custom-Commands',
  'File-Checkpoints', 'Auto-Memory', 'Troubleshooting',
  'Architecture', 'Adding-Tools', 'Hooks-and-Extensibility', 'Contributing',
];

// Sidebar position for each page (matches sidebars.js order)
const PAGE_META = {
  'Home':                   { label: 'Home',                      position: 1 },
  'Installation':           { label: 'Installation',              position: 1 },
  'Quick-Start':            { label: 'Quick Start',               position: 2 },
  'Model-Setup':            { label: 'Model Setup',               position: 3 },
  'Commands-and-Shortcuts': { label: 'Commands & Shortcuts',      position: 1 },
  'Tool-Reference':         { label: 'Tool Reference',            position: 2 },
  'Configuration':          { label: 'Configuration',             position: 3 },
  'Sessions-and-Context':   { label: 'Sessions & Context',        position: 4 },
  'Project-Instructions':   { label: 'Project Instructions (ORCA.md)', position: 5 },
  'Custom-Commands':        { label: 'Custom Commands',           position: 6 },
  'File-Checkpoints':       { label: 'File Checkpoints',          position: 7 },
  'Auto-Memory':            { label: 'Auto Memory',               position: 8 },
  'Troubleshooting':        { label: 'Troubleshooting',           position: 9 },
  'Architecture':           { label: 'Architecture',              position: 1 },
  'Adding-Tools':           { label: 'Adding Tools',              position: 2 },
  'Hooks-and-Extensibility':{ label: 'Hooks & Extensibility',     position: 3 },
  'Contributing':           { label: 'Contributing',              position: 4 },
};

function transformLinks(content) {
  // Transform wiki-style links [text](PageName) → [text](/docs/PageName)
  // Only transform links that match known page names (not URLs or anchors)
  return content.replace(
    /\[([^\]]+)\]\(([^)]+)\)/g,
    (match, text, target) => {
      // Skip URLs, anchors, and relative paths
      if (target.startsWith('http') || target.startsWith('#') || target.startsWith('/') || target.includes('.')) {
        return match;
      }
      // Only transform known page names
      if (KNOWN_PAGES.includes(target)) {
        return `[${text}](/docs/${target})`;
      }
      return match;
    }
  );
}

function buildFrontmatter(pageName) {
  const meta = PAGE_META[pageName];
  if (!meta) return '';

  const lines = ['---'];
  lines.push(`sidebar_label: "${meta.label}"`);
  lines.push(`sidebar_position: ${meta.position}`);

  if (pageName === 'Home') {
    lines.push('slug: "/"');
  }

  lines.push('---');
  lines.push('');
  return lines.join('\n');
}

function sync() {
  // Clean and recreate docs dir
  if (fs.existsSync(DOCS_DIR)) {
    fs.rmSync(DOCS_DIR, { recursive: true });
  }
  fs.mkdirSync(DOCS_DIR, { recursive: true });

  const files = fs.readdirSync(WIKI_DIR).filter(
    f => f.endsWith('.md') && !SKIP_FILES.includes(f)
  );

  let count = 0;
  for (const file of files) {
    const pageName = file.replace('.md', '');
    const srcPath = path.join(WIKI_DIR, file);
    const destPath = path.join(DOCS_DIR, file);

    let content = fs.readFileSync(srcPath, 'utf-8');
    content = transformLinks(content);

    const frontmatter = buildFrontmatter(pageName);
    const output = frontmatter + content;

    fs.writeFileSync(destPath, output, 'utf-8');
    count++;
  }

  console.log(`Synced ${count} wiki pages to website/docs/`);
}

sync();
