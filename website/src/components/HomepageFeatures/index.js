import clsx from 'clsx';
import styles from './styles.module.css';

const FeatureList = [
  {
    title: 'Local & Private',
    emoji: '\ud83d\udd12',
    description:
      'Runs entirely on your machine via LM Studio. Your code never leaves your network \u2014 no cloud APIs, no telemetry, complete privacy.',
  },
  {
    title: '39 Built-in Tools',
    emoji: '\ud83e\uddf0',
    description:
      'File operations, shell commands, Git, GitHub, web search, archive management, and more \u2014 all available out of the box.',
  },
  {
    title: 'Rich Terminal UI',
    emoji: '\ud83c\udfa8',
    description:
      'Syntax-highlighted code, colorized diffs, Markdown rendering, progress indicators, and streaming output \u2014 all in your terminal.',
  },
  {
    title: 'Multi-Model Support',
    emoji: '\ud83e\udd16',
    description:
      'Works with any OpenAI-compatible API. Auto-detects native tool calling or falls back to text-based mode for maximum compatibility.',
  },
  {
    title: 'Session & Memory',
    emoji: '\ud83e\udde0',
    description:
      'Persistent sessions with branching, file checkpoints with rollback, and auto-memory that learns your project patterns.',
  },
  {
    title: 'Extensible & Customizable',
    emoji: '\ud83d\udd27',
    description:
      'Custom slash commands, custom agents, MCP server integration, project instructions via ORCA.md, and permission glob patterns.',
  },
  {
    title: 'Autonomous Agent Loop',
    emoji: '\ud83d\udd04',
    description:
      'The AI plans and executes multi-step tasks autonomously, spawning sub-agents in parallel for complex workflows.',
  },
  {
    title: 'Safety First',
    emoji: '\ud83d\udee1\ufe0f',
    description:
      'Tiered permission system, sandbox mode, allowed-directory restrictions, and file checkpoints protect your codebase.',
  },
  {
    title: 'Cross-Platform & Open Source',
    emoji: '\ud83c\udf0d',
    description:
      'Single-file binary for Windows, macOS, and Linux. MIT licensed. Built with .NET 10 and Spectre.Console.',
  },
];

function Feature({emoji, title, description}) {
  return (
    <div className={clsx('col col--4')}>
      <div className={styles.featureCard}>
        <div className={styles.featureEmoji}>{emoji}</div>
        <h3>{title}</h3>
        <p>{description}</p>
      </div>
    </div>
  );
}

export default function HomepageFeatures() {
  return (
    <section className={styles.features}>
      <div className="container">
        <div className="row">
          {FeatureList.map((props, idx) => (
            <Feature key={idx} {...props} />
          ))}
        </div>
      </div>
    </section>
  );
}
