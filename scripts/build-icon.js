const fs = require('fs');
const path = require('path');
const { makeFolderICO } = require('../folder-icon');

const root = path.join(__dirname, '..');
const icoPath = path.join(root, 'icon.ico');
fs.writeFileSync(icoPath, makeFolderICO([16, 24, 32, 48, 64, 128, 256]));
console.log('Wrote', icoPath);
