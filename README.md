# ZapretManager

Windows-приложение для установки, обновления, запуска, проверки и отката сборок zapret на базе [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube).

`ZapretManager` нужен для того, чтобы работать со сборкой zapret из одного окна, без постоянного переключения между bat-файлами, папками, списками доменов и ручной диагностикой.

## Что умеет программа

- автоматически или вручную находить рабочую папку zapret
- скачивать, обновлять, удалять и откатывать сборку
- запускать выбранный конфиг и останавливать `winws`
- устанавливать и удалять службу Windows
- подбирать конфиг через автоматический режим
- проверять конфиги по HTTP, TLS 1.2, TLS 1.3 и ping
- показывать подробные результаты проверки по каждой цели
- скрывать нерабочие конфиги и возвращать их из отдельного окна
- редактировать `targets.txt`, `hosts`, включённые и исключённые домены, пользовательские IPSet-подсети
- управлять DNS-профилями, пользовательским DNS и DoH
- очищать кэш Discord из интерфейса программы
- запускаться в трей, работать в single-instance режиме и переключать светлую / тёмную тему

## Чем это удобно

- не нужно вручную вспоминать, какой bat-файл запускать
- можно быстро сравнить конфиги и увидеть, что реально работает лучше
- служба, DNS, диагностика и списки управляются из одной программы
- обновление и откат сборки делаются без ручной возни с папками

## Если не хочется разбираться вручную

Если ты вообще не хочешь вникать в конфиги, bat-файлы и дополнительные настройки, в программе есть кнопка `Автоматический режим`.

Как это работает на практике:

- если сборка zapret ещё не установлена, программа предложит выбрать папку и сама скачает туда свежую версию
- если сборка уже подключена, этот шаг пропускается
- автоматически подготавливает окружение: обновляет `IPSet` и `hosts`
- запускает полную проверку доступных конфигов
- сравнивает результаты HTTP, TLS 1.2, TLS 1.3 и ping
- выбирает лучший полностью подходящий конфиг
- сразу устанавливает его как службу Windows и запускает её

То есть во многих случаях для первого старта достаточно просто запустить программу и нажать `Автоматический режим`.

## Для кого это

`ZapretManager` подойдёт тем, кто:

- использует zapret на Windows
- хочет быстро запускать и тестировать разные конфиги
- не хочет вручную редактировать служебные файлы в папке сборки
- хочет держать DNS, диагностику и списки целей под рукой

## Быстрый старт

1. Скачай `ZapretManager.exe` из раздела **Releases**.
2. Запусти программу.
3. Укажи существующую папку zapret или скачай свежую сборку через интерфейс.
4. Выбери конфиг и проверь его, либо запусти автоматический режим.

## Работа через трей

Программа умеет нормально жить в системном трее:

- сворачиваться в трей вместо полного закрытия
- быстро открываться обратно по значку
- давать быстрый доступ к основным действиям
- не мешать в панели задач, если хочется оставить её работать в фоне

Это удобно, если zapret уже запущен и программу нужно просто держать под рукой.

## Системные требования

- Windows 10 / Windows 11
- x64
- права администратора для части действий со службой, драйвером и системными файлами

## Важно про Защитник Windows

Сам `ZapretManager` обычно не вызывает предупреждений антивируса, но Защитник Windows может ругаться на файлы самой сборки zapret.

Если во время установки, обновления или запуска сборки что-то блокируется, лучше:

- добавить папку сборки zapret в исключения Защитника Windows
- либо временно отключить защиту на время установки или обновления сборки через программу

Это относится именно к файлам zapret, а не к интерфейсу `ZapretManager`.

## Основа проекта

- сборка Flowseal: [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube)
- оригинальный проект zapret: [bol-van/zapret](https://github.com/bol-van/zapret)

## Скриншоты

### Главное окно

![Главное окно](Assets/README/main-dark.png)

Поиск сборки, запуск конфигов, авто-режим, служба, обновление и результаты проверки собраны в одном окне. Это основная рабочая точка программы.

### Светлая тема

![Светлая тема](Assets/README/main-light.png)

У `ZapretManager` есть не только тёмная, но и полноценная светлая тема. Все основные сценарии, таблицы и окна поддерживают переключение темы без отдельной настройки интерфейса.

### Диагностика системы

![Диагностика системы](Assets/README/diagnostics.png)

Показывает, что может мешать работе сборки: службы Windows, DNS, WinDivert, `hosts` и другие системные конфликты. Это удобно перед первым запуском и после обновления.

### Подробные результаты проверки

![Подробные результаты](Assets/README/probe-details.png)

Для каждого конфига можно открыть детальные результаты по целям: HTTP, TLS 1.2, TLS 1.3, ping и итоговый статус. Это помогает быстро понять, что именно работает, а что нет.

### Редактор целей

![Редактор targets.txt](Assets/README/targets-editor.png)

Служебные списки можно редактировать прямо из программы, без ручного открытия файлов в папке сборки. Это же касается других списков: `hosts`, доменов и пользовательских IPSet-подсетей.

## Полезные инструменты

Помимо настройки и проверки конфигов, в программе есть и прикладные утилиты для повседневного использования. Например, можно очистить кэш Discord прямо из интерфейса, не ища нужные папки вручную.

## Важно

`ZapretManager` не заменяет сам проект zapret и не является его официальной частью. Это отдельный Windows-менеджер для более удобной работы со сборками.

---

## Дисклеймер

> ⚠️ В случае введения юридических или технических ограничений со стороны провайдеров или государственных органов автор не несёт ответственности за последствия использования программы. Скачивая и используя приложение, вы соглашаетесь с этим.

---

# ZapretManager

Windows application for installing, updating, launching, testing and rolling back zapret builds based on [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube).

`ZapretManager` is designed to let you work with a zapret build from one window, without constantly switching between bat files, folders, domain lists and manual diagnostics.

## What the program can do

- find the zapret folder automatically or manually
- download, update, remove and roll back the build
- launch the selected config and stop `winws`
- install and remove the Windows service
- pick a config through automatic mode
- test configs using HTTP, TLS 1.2, TLS 1.3 and ping
- show detailed probe results for every target
- hide broken configs and restore them from a separate window
- edit `targets.txt`, `hosts`, included and excluded domains, and custom IPSet subnets
- manage DNS profiles, custom DNS and DoH
- clear Discord cache directly from the program
- run in the tray, stay single-instance and switch between light and dark themes

## Why this is convenient

- no need to remember which bat file to launch manually
- you can compare configs quickly and see what actually works better
- service control, DNS, diagnostics and list editing are all available in one place
- updating and rolling back builds does not require manual folder juggling

## If you do not want to configure everything manually

If you do not want to deal with configs, bat files and extra settings, the program has an `Automatic mode` button.

In practice it works like this:

- if zapret is not installed yet, the program asks where to place it and downloads the latest build there
- if a build is already connected, this step is skipped
- automatically prepares the environment by updating `IPSet` and `hosts`
- runs a full check of available configs
- compares HTTP, TLS 1.2, TLS 1.3 and ping results
- selects the best fully suitable config
- installs it as a Windows service and starts it immediately

So in many cases the first start is as simple as launching the program and pressing `Automatic mode`.

## Who this is for

`ZapretManager` is useful for people who:

- use zapret on Windows
- want to launch and test different configs quickly
- do not want to edit service files manually inside the build folder
- want DNS, diagnostics and target lists close at hand

## Quick start

1. Download `ZapretManager.exe` from **Releases**.
2. Launch the program.
3. Select an existing zapret folder or download a fresh build from the interface.
4. Pick a config and test it, or just use automatic mode.

## Tray workflow

The program is designed to work comfortably from the system tray:

- it can minimize to tray instead of closing completely
- it can be restored quickly from the tray icon
- the tray menu provides quick access to common actions
- it can stay in the background without getting in the way on the taskbar

This is useful when zapret is already running and you just want to keep the manager nearby.

## System requirements

- Windows 10 / Windows 11
- x64
- administrator rights for some actions involving services, drivers and system files

## Important note about Windows Defender

`ZapretManager` itself usually does not trigger antivirus warnings, but Windows Defender may complain about files from the zapret build.

If something gets blocked during installation, updating or launching the build, it is better to:

- add the zapret build folder to Windows Defender exclusions
- or temporarily disable protection while installing or updating the build from the program

This applies to zapret files, not to the `ZapretManager` interface itself.

## Project base

- Flowseal build: [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube)
- original zapret project: [bol-van/zapret](https://github.com/bol-van/zapret)

## Screenshots

### Main window

![Main window](Assets/README/main-dark.png)

Build discovery, config launching, automatic mode, service controls, updating and test results are all gathered in one place. This is the main working area of the program.

### Light theme

![Light theme](Assets/README/main-light.png)

`ZapretManager` includes not only a dark theme but also a full light theme. Core scenarios, tables and windows support theme switching without a separate interface mode.

### System diagnostics

![System diagnostics](Assets/README/diagnostics.png)

Shows what may interfere with the build: Windows services, DNS, WinDivert, `hosts` and other system conflicts. This is especially useful before the first start and after updating.

### Detailed probe results

![Detailed probe results](Assets/README/probe-details.png)

For every config you can open a detailed view of targets, HTTP, TLS 1.2, TLS 1.3, ping and the final status. This helps you understand what works and what does not.

### Target editor

![targets.txt editor](Assets/README/targets-editor.png)

Service lists can be edited directly from the program without manually opening files inside the build folder. The same applies to `hosts`, domain lists and custom IPSet subnets.

## Useful tools

Besides build management and config testing, the program also includes practical utility actions. For example, you can clear Discord cache directly from the interface without hunting for the correct folders manually.

## Important

`ZapretManager` does not replace the zapret project and is not an official part of it. It is a separate Windows manager created to make working with zapret builds more convenient.

---

## Disclaimer

> ⚠️ If providers or government bodies introduce legal or technical restrictions, the author is not responsible for any consequences of using this software. By downloading and using the application, you agree to this.
