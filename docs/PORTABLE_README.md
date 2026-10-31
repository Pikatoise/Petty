# Petty 1.0.0 — Windows x64

## Русский

Распакуйте весь ZIP в отдельную папку и запустите **Petty.exe**. Устанавливать .NET
и запускать от администратора не нужно. Windows 11 — основная платформа;
Windows 10 поддерживается без гарантии и здесь не проверялась. Питомец живёт
на основном мониторе. В комплекте 8 пород кошек и собак, по 5 окрасов.

При первом запуске выберите вид, породу, окрас и имя. Наведите курсор на питомца:
мяч — играть, ладонь — погладить. Клик по миске добавляет 25 единиц еды; во время
насыпания повторные клики игнорируются. Миску можно перетаскивать. Правый клик
по значку в трее открывает настройки и выход. Закрытие настроек оставляет питомца
работать; выход из трея сначала проигрывает уход. Потребности меняются при работе
Petty, сохраняются при закрытии и не снижаются во сне компьютера.

Автозапуск включён по умолчанию после успешного создания/запуска питомца.
Отключается в настройках. После переноса папки запустите Petty вручную один раз,
чтобы обновить путь автозапуска. Данные и журнал находятся в
`%LOCALAPPDATA%\Petty`; ZIP не содержит ваших сохранений.

Обновление: скачайте новый ZIP вручную с GitHub Releases, завершите Petty через
трей, замените файлы приложения вместе с Assets и запустите снова. Папку данных
не удаляйте. Для удаления отключите автозапуск, выйдите и удалите папку приложения.

Ручная приёмка ещё требуется: см. [чек-лист](RELEASE_CHECKLIST.md). Реальные
сон/выход из Windows, перезапуск Explorer, разные DPI и длительная работа здесь
не подтверждены. Графика процедурная; внешний вид и плавность требуют вашей оценки.

## English

Extract the entire ZIP into its own folder and run **Petty.exe**. No .NET installation
or administrator rights are required. Windows 11 is primary; Windows 10 is best effort
and untested here. Only the primary monitor is used. Includes 8 breeds and 5 coats each.

Choose species, breed, coat and name. Hover the pet for Play (ball) and Pet (hand).
A bowl click adds 25 food; repeated clicks during pouring are ignored. Drag to move
the bowl. Right-click the tray icon for Settings or animated Exit. Closing Settings
keeps Petty running. Needs decay while running, freeze while closed or PC asleep.

Autostart defaults to enabled after a successful pet startup; disable it in Settings.
After moving the folder, launch once manually to repair the startup path. Data/logs
stay in `%LOCALAPPDATA%\Petty`, outside this ZIP.

Updates are manual: download a newer ZIP from GitHub Releases, exit through the tray,
replace application files including Assets, reopen. Keep the data folder. To uninstall,
disable autostart, exit, then delete the extracted application folder.

Owner acceptance is pending: [manual checklist](RELEASE_CHECKLIST.md). Actual Windows
sleep/logoff, Explorer restart, DPI changes and prolonged use remain unverified here.
Art is procedural; likeness and animation quality require visual acceptance.
See [Windows details](WINDOWS_RELEASE.md) for diagnostics and known shell limitations.
