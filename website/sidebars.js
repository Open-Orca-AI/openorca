/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  docsSidebar: [
    'Home',
    {
      type: 'category',
      label: 'Getting Started',
      items: ['Installation', 'Quick-Start', 'Model-Setup'],
    },
    {
      type: 'category',
      label: 'User Guide',
      items: [
        'Commands-and-Shortcuts',
        'Tool-Reference',
        'Configuration',
        'Sessions-and-Context',
        'Project-Instructions',
        'Custom-Commands',
        'File-Checkpoints',
        'Auto-Memory',
        'Troubleshooting',
      ],
    },
    {
      type: 'category',
      label: 'Developer Guide',
      items: [
        'Architecture',
        'Adding-Tools',
        'Hooks-and-Extensibility',
        'Contributing',
      ],
    },
  ],
};

module.exports = sidebars;
