const fs = require('fs');
const { patchMsiProject } = require('./msi-autostart');

module.exports = {
  appId: 'ru.bzklinika.emergencyfolder',
  productName: 'Emergency folder',
  directories: {
    output: 'dist'
  },
  win: {
    icon: 'icon.ico',
    target: [
      {
        target: 'msi',
        arch: ['x64']
      }
    ]
  },
  msi: {
    upgradeCode: '{8A3C7B5E-4F2D-4A1B-9C6E-1D2F3A4B5C6D}',
    oneClick: true,
    perMachine: true,
    createDesktopShortcut: false,
    createStartMenuShortcut: true,
    runAfterFinish: true,
    artifactName: 'Emergency-folder-${version}.${ext}'
  },
  files: [
    'main.js',
    'preload.js',
    'config.js',
    'fab.html',
    'folder-icon.js',
    'launch-user.ps1'
  ],
  async msiProjectCreated(projectFile) {
    const xml = fs.readFileSync(projectFile, 'utf8');
    fs.writeFileSync(projectFile, patchMsiProject(xml));
  }
};
