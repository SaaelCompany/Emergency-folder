#!/usr/bin/env python3
"""Дописывает в MSI то, что wixl на Linux не сохраняет сам."""

import subprocess
import sys
import tempfile
from pathlib import Path

MANUFACTURER = "Будь Здоров"
DESCRIPTION = "Общий рабочий стол"


def run(msi: Path, *args: str) -> None:
    subprocess.check_call(["msibuild", str(msi), *args])


def export(msi: Path, table: str) -> bytes:
    return subprocess.check_output(["msiinfo", "export", str(msi), table])


def main() -> None:
    msi = Path(sys.argv[1])
    with tempfile.NamedTemporaryFile(suffix=".idt") as handle:
        handle.write(b"\r\n\r\n65001\t_ForceCodepage\r\n")
        handle.flush()
        run(msi, "-i", handle.name)

    run(
        msi,
        "-q",
        "INSERT INTO `CustomAction` (`Action`, `Type`, `Source`, `Target`) "
        "VALUES ('CloseEmergencyFolder', 98, 'System64Folder', "
        "'taskkill.exe /F /T /IM \"Emergency folder.exe\"')",
        "-q",
        "INSERT INTO `LaunchCondition` (`Condition`, `Description`) "
        "VALUES ('Installed OR (VersionNT >= 601)', 'Windows 7 and above is required.')",
        "-q",
        f"UPDATE `Property` SET `Value`='{MANUFACTURER}' WHERE `Property`='Manufacturer'",
        "-q",
        f"UPDATE `Shortcut` SET `Description`='{DESCRIPTION}' WHERE `Shortcut`='AutostartShortcut'",
        "-q",
        "UPDATE `File` SET `FileName`='EMERGE~1.EXE|Emergency folder.exe' WHERE `File`='MainExe'",
        "-q",
        "UPDATE `Directory` SET `DefaultDir`='EMERGE~1|Emergency folder' WHERE `Directory`='INSTALLFOLDER'",
        "-q",
        "UPDATE `Shortcut` SET `Name`='EMERGE~1|Emergency folder'",
    )

    custom = export(msi, "CustomAction")
    props = export(msi, "Property")
    shortcuts = export(msi, "Shortcut")
    sequence = export(msi, "InstallExecuteSequence")
    if b"CloseEmergencyFolder\t98\tSystem64Folder\t" not in custom:
        raise SystemExit("В MSI нет действия закрытия программы")
    if MANUFACTURER.encode() not in props:
        raise SystemExit("В MSI не сохранился издатель")
    if DESCRIPTION.encode() not in shortcuts:
        raise SystemExit("В MSI не сохранилось описание ярлыка автозагрузки")
    if b"RunAfterFinish\tNOT Installed\t6601" not in sequence.replace(b"\r", b""):
        raise SystemExit("Тихий запуск после установки не попал в последовательность")
    if b"ALLUSERS\t1" not in props:
        raise SystemExit("Установщик не помечен как установка на компьютер")


if __name__ == "__main__":
    main()
