# Emergency folder

Приложение сидит в трее и открывает общий рабочий стол (`C:\Users\Public\Desktop`). После установки оно сразу запускается и попадает в общую автозагрузку всех пользователей.

## Тихая установка (SCCM)

Импортируйте MSI в SCCM как установщик Windows. Если команда задаётся вручную:

```bat
msiexec /i "Emergency-folder-2.1.0.msi" /qn /norestart
```

Журнал установки:

```bat
msiexec /i "Emergency-folder-2.1.0.msi" /qn /norestart /l*v "%WINDIR%\Temp\EmergencyFolder-install.log"
```

Удаление:

```bat
msiexec /x "Emergency-folder-2.1.0.msi" /qn /norestart
```

`/qn` и `/quiet` — полностью тихий режим, `/qb` — только индикатор. Перезагрузка не требуется.

Установщик ставит программу в `C:\Program Files\Emergency folder` и создаёт ярлык в общей автозагрузке:

`C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup\Emergency folder.lnk`

Ярлык удаляется вместе с программой. Если установка идёт от SYSTEM (обычный режим SCCM), установщик завершает уже запущенную копию, затем программа стартует в сессии вошедшего пользователя. Если в момент установки никто не вошёл, программа запустится при следующем входе через этот ярлык.

Код обновления MSI: `{8A3C7B5E-4F2D-4A1B-9C6E-1D2F3A4B5C6D}`.
