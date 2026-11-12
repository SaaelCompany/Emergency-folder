// Правки WiX-проекта electron-builder: тихий запуск и общая автозагрузка.
function patchMsiProject(xml) {
  if (!xml.includes('Id="runAfterFinish"')) {
    throw new Error('В проекте MSI нет действия runAfterFinish');
  }

  const silentCondition = /NOT Installed AND UILevel\s*(?:>=|&gt;=)\s*4/;
  let patched = xml;
  if (silentCondition.test(xml)) {
    patched = xml.replace(silentCondition, 'NOT Installed');
  } else if (!xml.includes('>NOT Installed<')) {
    throw new Error('Не найдено условие UILevel запуска после установки');
  }

  if (!patched.includes('Id="CloseEmergencyFolder"')) {
    patched = patched.replace(
      '<CustomAction Id="runAfterFinish"',
      '<CustomAction Id="CloseEmergencyFolder" Directory="SystemFolder" ExeCommand="taskkill.exe /F /T /IM &quot;Emergency folder.exe&quot;" Execute="immediate" Return="ignore"/>\n      <CustomAction Id="runAfterFinish"'
    );
  }
  if (!patched.includes('Action="CloseEmergencyFolder"')) {
    patched = patched.replace(
      '<InstallExecuteSequence>',
      '<InstallExecuteSequence>\n        <Custom Action="CloseEmergencyFolder" Before="InstallFiles">1</Custom>'
    );
  }

  if (!patched.includes('Id="SystemFolder"')) {
    patched = insertDirectory(patched, 'SystemFolder');
  }
  if (!patched.includes('Id="StartupFolder"')) {
    patched = insertDirectory(patched, 'StartupFolder');
  }

  if (!patched.includes('Id="autostartShortcut"')) {
    const iconMatch = patched.match(/<Icon Id="([^"]+)"/);
    const iconAttr = iconMatch ? ` Icon="${iconMatch[1]}"` : '';
    const shortcut = `<Shortcut Id="autostartShortcut" Directory="StartupFolder" Name="Emergency folder" Description="Общий рабочий стол" WorkingDirectory="APPLICATIONFOLDER" Advertise="yes"${iconAttr}/>`;
    const closeFile = patched.indexOf('</File>');
    if (closeFile < 0) {
      throw new Error('Не найден исполняемый файл, к которому нужно привязать ярлык автозагрузки');
    }
    patched = patched.slice(0, closeFile) + shortcut + '\n          ' + patched.slice(closeFile);
  }

  return patched;
}

function insertDirectory(xml, id) {
  const programMenu = /<Directory Id="ProgramMenuFolder"\s*\/>/;
  if (!programMenu.test(xml)) {
    throw new Error('Не найден ProgramMenuFolder, некуда добавить ' + id);
  }
  return xml.replace(programMenu, `<Directory Id="ProgramMenuFolder"/>\n      <Directory Id="${id}"/>`);
}

module.exports = { patchMsiProject };
