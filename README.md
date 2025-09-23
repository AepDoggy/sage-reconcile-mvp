# Sage Reconcile Engine (MVP)

**Sage** — это простой движок reconcile «задекларировал в YAML → получил на хостах»,
с CLI на .NET 8 и выполнением через Ansible. Поддерживает:

- **sysctl** (произвольные ключи)
- **/etc/hosts** (управляемый блок)
- **apt-пакеты** (present/absent)
- **docker-контейнеры** (image, ports, env, volumes, command, limits)
- **много хостов** с общими **defaults** + переопределения на хостах
- команды `validate`, `plan`, `apply`, `reconcile`
- периодический авто-**reconcile** с контроллера через **systemd timer**

---

## Содержание

- [Требования](#требования)
- [Быстрый старт](#быстрый-старт)
- [Команды CLI](#команды-cli)
- [Формат YAML (Config)](#формат-yaml-config)
- [Как это работает внутри](#как-это-работает-внутри)
- [Деплой CLI в /opt/sage + systemd-таймер](#деплой-cli-в-optsage--systemd-таймер)
- [Проверка и отладка](#проверка-и-отладка)
- [Чек‑лист тестов (drift → reconcile)](#чеклист-тестов-drift--reconcile)
- [Типовые проблемы и решения](#типовые-проблемы-и-решения)
- [Архитектура и структура репозитория](#архитектура-и-структура-репозитория)
- [Тесты](#Тесты)
- [Ограничения и планы](#ограничения)
- [Планы](#планы)
- [Скриншоты работы программы](#скриншоты)
- [Маленькая демонстрация работы консоли](#Видеодемонстрация)

---

## Требования

**Контроллер (где запускается CLI):**
- Linux (проверено на Ubuntu/Mint)
- .NET SDK **8.0**
- Ansible **2.14+**
- Коллекции Ansible: `community.docker`, `ansible.posix`
- SSH‑доступ к целевым хостам по ключу (пароль не обязателен)

Установка на контроллере:
```bash
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0 ansible python3-pip jq git openssh-client
ansible-galaxy collection install community.docker ansible.posix
```

**Целевые хосты (Ubuntu 24.04): Я прогонял MVP на двух ВМ в Yandex Cloud с Ubuntu:**

- Доступны по публичному IP/имени
- Python3 установлен (если нет — playbook поставит)
- Docker установлен и запущен (для docker‑apps)
- Пользователь с sudo (лучше **NOPASSWD**)

Настройка NOPASSWD (пример):
```bash
echo "user ALL=(ALL) NOPASSWD:ALL" | sudo tee /etc/sudoers.d/user
sudo visudo -cf /etc/sudoers.d/user
```

> Альтернатива — запуск с `--ask-become-pass` у `apply/reconcile`.

---

## Быстрый старт

1) Клонировать репозиторий и перейти в него:
```bash
git clone <repo-url> sage-reconcile-mvp
cd sage-reconcile-mvp
```

2) Заполнить `examples/config.yaml` (см. пример ниже) и проверить SSH‑ключ.

3) Валидация и dry‑run:
```bash
dotnet run --project src/SageCli -- validate -f examples/config.yaml

# plan = ansible --check --diff
dotnet run --project src/SageCli -- plan -f examples/config.yaml -v --forks 5
```

4) Применение и reconcile вручную:
```bash
# применить desired state
dotnet run --project src/SageCli -- apply -f examples/config.yaml -v --forks 5

# обнаружить дрейф (plan) и при наличии изменений выполнить apply
dotnet run --project src/SageCli -- reconcile -f examples/config.yaml -v --forks 5
```

> Временные артефакты и логи будут в `.sage-tmp/run-YYYYMMDDHHMMSS/`.

---

## Команды CLI

```text
sage validate  -f <config.yaml>
sage plan      -f <config.yaml> [--limit host1,host2] [--forks N] [-v]
sage apply     -f <config.yaml> [--limit host1,host2] [--forks N] [-v] [--ask-become-pass|-K]
sage reconcile -f <config.yaml> [--limit host1,host2] [--forks N] [-v] [--ask-become-pass|-K]
```

- `validate` — проверка структуры/значений YAML (FluentValidation)
- `plan` — `ansible-playbook --check --diff` (без изменений на хостах)
- `apply` — реальное применение
- `reconcile` — сначала `plan`, парсинг `PLAY RECAP` (сумма `changed=` по хостам),
  если `changed > 0` → выполняет `apply`, иначе сообщает «ничего не делать»

Флаги:
- `-f, --file` — путь к конфигурации
- `--limit` — ограничение на подмножество хостов (имена из `hosts[].name`)
- `--forks` — параллельные форки Ansible
- `-v` — подробный лог Ansible
- `-K, --ask-become-pass` — запрос пароля sudo (если нет NOPASSWD)

Логи запуска:
- `ansible.plan.stdout.log` / `ansible.plan.stderr.log`
- `ansible.apply.stdout.log` / `ansible.apply.stderr.log`

---

## Формат YAML (Config)

### Поля верхнего уровня
```yaml
version: 1
ssh:
  user: user          # SSH-пользователь
  port: 22            # SSH-порт
  private_key_path: ~/.ssh/id_ed25519
  become: true        # выполнять задачи через sudo

# Необязательные общие значения (defaults) — применяются ко всем хостам,
# а внутри host можно их переопределить точечно
defaults:
  sysctl: { ... }
  hosts_entries: { managed_block_name: ..., entries: [ ... ] }
  packages: { present: [ ... ], absent: [ ... ] }
  docker_apps: [ ... ]

hosts:
  - name: yc-ubuntu-1
    address: 203.0.113.10
    # ниже — опциональные переопределения поверх defaults
    sysctl: { ... }
    hosts_entries: { ... }
    packages: { present: [...], absent: [...] }
    docker_apps: [ ... ]

  - name: yc-ubuntu-2
    address: 203.0.113.11
```

### Пример полного файла (2 хоста + defaults)
```yaml
version: 1
ssh:
  user: user
  port: 22
  private_key_path: ~/.ssh/id_ed25519
  become: true

defaults:
  sysctl:
    vm.swappiness: "10"
    net.ipv4.ip_forward: "1"
  hosts_entries:
    managed_block_name: "sre-managed"
    entries:
      - "10.10.0.5 db.internal"
      - "10.10.0.6 cache.internal"
  packages:
    present: ["htop", "curl", "nginx=1.24.*"]
    absent:  ["snapd"]
  docker_apps:
    - name: "node-exporter"
      image: "prom/node-exporter:v1.8.1"
      restart_policy: "unless-stopped"
      ports: ["9100:9100"]
      cpus: 0.1
      memory: "64m"

hosts:
  - name: yc-ubuntu-1
    address: 84.201.135.32

  - name: yc-ubuntu-2
    address: 89.169.135.28
```

### Спецификация полей

- **sysctl** — произвольный словарь ключ→значение (строки). Хранится в `/etc/sysctl.d/99-sre.conf`,
  изменения применяются сразу (`reload: true`).
- **hosts_entries** — управляется только блок между маркерами в `/etc/hosts`:
  ```
  # --- SRE BEGIN <managed_block_name> ---
  ...здесь ваши строки...
  # --- SRE END <managed_block_name> ---
  ```
- **packages.present/absent** — ставим/удаляем через `apt`.
- **docker_apps[]** — для каждого контейнера:
  - `name` (string) — имя контейнера
  - `image` (string) — образ
  - `restart_policy` (string, default `unless-stopped`)
  - `env` (map) — переменные окружения
  - `command` (list) — команда
  - `volumes` (list) — маппинги директорий `host:container[:ro]`
  - `ports` (list) — публикации портов `host:container`
  - `cpus` (float) — лимит CPU
  - `memory` (string) — лимит памяти, например `64m`, `1g`
  - `extra_hosts` (list) — дополнительные хосты

> Текущая реализация всегда приводит контейнер к состоянию **started** (нет поля `state`).
> Если контейнер остановить вручную — `apply/reconcile` его поднимет. Удаление по YAML пока не предусмотрено.

---

## Как это работает внутри

1. **CLI** читает YAML (YamlDotNet), валидирует (FluentValidation).
2. Генерирует Ansible‑артефакты в `.sage-tmp/run-<ts>/`:
   - `inventory.ini` (all‑группа, ssh‑параметры)
   - `host_vars/<host>.yml` (слияние `defaults` + оверрайды хоста)
   - `site.yml` (копия из `ansible_templates/site.yml`)
3. Запускает `ansible-playbook`:
   - `plan` → `--check --diff`
   - `apply` → без `--check`
4. `reconcile` делает `plan`, парсит `PLAY RECAP` → если суммарный `changed > 0`,
   выполняет `apply`.

В `site.yml` реализованы роли:
- `ansible.posix.sysctl` — запись в `/etc/sysctl.d/99-sre.conf` + reload
- `blockinfile` — управляемый блок в `/etc/hosts`
- `apt` — пакеты present/absent
- `community.docker.docker_container` — контейнеры (pull, create/update, run)

---

## Деплой CLI в /opt/sage + systemd-таймер

> Этот шаг не обязателен для ручного одиночного запуска. Нужен для безнадзорного reconcile с контроллера (каждые N минут).

### Публикация
Установка (Beta installer):
Инсталлер не до конца протестирован 

Из репо выполнять 
опционально: выбрать свой config и ключ
```bash
chmod +x scripts/install_sage.sh

OVERRIDE_KEY_PATH=/root/.ssh/id_ed25519 \
DISABLE_HOST_KEY_CHECKING=true \
FORKS=5 \
./install_sage.sh examples/config.yaml
```

Ручная публикация и установка демона

 (framework‑dependent, требуется `dotnet` на контроллере):
```bash
# собрать и сложить артефакты в dist/
dotnet publish src/SageCli -c Release -o ./dist/sage

# развернуть в /opt/sage
sudo mkdir -p /opt/sage
sudo cp -r ./dist/sage/* /opt/sage/

# шаблоны и пример конфига
sudo mkdir -p /opt/sage/ansible_templates /opt/sage/examples
sudo cp -r ansible_templates/* /opt/sage/ansible_templates/
sudo cp examples/config.yaml /opt/sage/examples/config.yaml
```

Поменять путь private_key_path:  в config.yaml в /opt/sage/examples

Пара замечаний:
- В `config.yaml` путь `ssh.private_key_path` должен быть доступен пользователю сервиса (обычно `root`).
  Можно прописать, например, `/home/user/.ssh/id_ed25519`.
- Для строгой проверки ключей SSH: заполните `/root/.ssh/known_hosts`.
  Быстрый способ:
  ```bash
  sudo mkdir -p /root/.ssh
  sudo ssh-keyscan -H <IP1> <IP2> | sudo tee -a /root/.ssh/known_hosts
  sudo chmod 644 /root/.ssh/known_hosts
  ```

### Unit и Timer

`/etc/systemd/system/sage-reconcile.service`:
```ini
[Unit]
Description=Sage Reconcile (plan->apply if drift)
After=network-online.target
Wants=network-online.target

[Service]
Type=oneshot
WorkingDirectory=/opt/sage
Environment=PATH=/usr/local/\ sbin:/usr/local/bin:/usr/sbin:/usr/bin
# опционально, чтобы не спотыкаться о ключи хостов
Environment=ANSIBLE_HOST_KEY_CHECKING=False
ExecStart=/usr/bin/dotnet /opt/sage/SageCli.dll reconcile -f /opt/sage/examples/config.yaml --forks 5
```

`/etc/systemd/system/sage-reconcile.timer`:
```ini
[Unit]
Description=Run Sage Reconcile every 5 minutes

[Timer]
OnBootSec=2min
OnUnitActiveSec=5min
AccuracySec=30s
Unit=sage-reconcile.service

[Install]
WantedBy=timers.target
```

Активация и проверка:
```bash
sudo systemctl daemon-reload
sudo systemctl enable --now sage-reconcile.timer
systemctl status sage-reconcile.timer

# разовый прогон без ожидания таймера
sudo systemctl start sage-reconcile.service
journalctl -u sage-reconcile.service -n 200 -f
```

**Отключить/включить таймер:**
```bash
# пауза MVP
sudo systemctl disable --now sage-reconcile.timer
sudo systemctl stop sage-reconcile.service

# вернуться позже
sudo systemctl enable --now sage-reconcile.timer
```

**Полностью заблокировать запуск (опционально):**
```bash
sudo systemctl mask sage-reconcile.service
sudo systemctl unmask sage-reconcile.service
```

**Продвинутые варианты (опционально):**
- `--limit <host>` внутри `ExecStart` — запускать только для 1 хоста
- параметризованные юниты `sage-reconcile@<host>.service/.timer` — по одному на хост
- два `ExecStart`, первый с префиксом `-` (игнорировать ошибку одного из хостов)

Архив sage_Published_with_demon.tar.gz - пример как будет выглядет опубликованный Sage с демоном 

---

## Проверка и отладка

Посмотреть логи последнего запуска:
```bash
# CLI вывод в stdout (dotnet run)
# логи ansible в рабочей папке:
ls -la .sage-tmp/
cat .sage-tmp/run-*/ansible.plan.stdout.log | tail -n 100
cat .sage-tmp/run-*/ansible.apply.stdout.log | tail -n 100
```

При запуске через systemd:
```bash
journalctl -u sage-reconcile.service -n 200 -f
systemctl status sage-reconcile.service
systemctl status sage-reconcile.timer
```

---

## Чек‑лист тестов (drift → reconcile)

1. **sysctl**
   ```bash
  На целевом хосте - sudo sysctl -w vm.swappiness=77
   
  На контролирующем хосте # reconcile (вручную или дождаться таймера)
   dotnet run --project src/SageCli -- reconcile -f examples/config.yaml
   sysctl -n vm.swappiness  # ⇒ 10
   ```
2. **/etc/hosts**
   ```bash
   echo "127.0.0.1 bad.entry" | sudo tee -a /etc/hosts
   reconcile → блок SRE вернётся к целевому
   ```
3. **apt‑пакеты**
   ```bash
   sudo apt-get -y remove htop
   reconcile → htop снова установлен
   ```
4. **docker‑контейнер**
   ```bash
   docker stop node-exporter
   reconcile → контейнер снова в started
   ```

Результат успешного `plan` без дрейфа:
```
PLAY RECAP
host1 : ok=6 changed=0 failed=0 ...
host2 : ok=6 changed=0 failed=0 ...
```

---

## Типовые проблемы и решения

- **Host key verification failed** (через systemd):
  - Заполнить `/root/.ssh/known_hosts` через `ssh-keyscan`, или
  - Включить `Environment=ANSIBLE_HOST_KEY_CHECKING=False` в unit.

- **Permission denied (publickey)** / **no such identity**:
  - Убедитесь, что `ssh.private_key_path` указывает на существующий ключ и читаем пользователем сервиса.
  - Проверьте, что публичный ключ в `~user/.ssh/authorized_keys` на целевом.

- **MODULE FAILURE: setup** при `Gathering Facts`:
  - Обычно это временный разрыв соединения/мультиплексор SSH. Повторный запуск проходит успешно.

- **docker_container**: контейнер не стартует
  - Проверьте, что Docker установлен и демон запущен: `sudo systemctl status docker`
  - Посмотрите логи контейнера: `docker logs <name>`

- **apt**: зависания/блокировки
  - Возможно, идёт `apt` из других процессов. Подождите или очистите `dpkg`‑lock.

- **CRLF → LF**

  - Если видите «cannot execute: required file not found» / ^M:

   ```bash
   sed -i 's/\r$//' install_sage.sh
   ```
---

## Архитектура и структура репозитория

```
.
├─ src/SageCli/            # .NET 8 CLI
│  ├─ Models/              # модели YAML
│  ├─ Validation/          # FluentValidation
│  ├─ Config/              # загрузка YAML
│  └─ Ansible/             # генерация артефактов, раннер, drift‑детектор
├─ ansible_templates/
│  └─ site.yml             # общий плейбук (sysctl, hosts, apt, docker)
├─ examples/
│  └─ config.yaml          # пример конфига (2 хоста + defaults)
├─ docs/                   # (опционально) YAML_SPEC.md, TESTS.md, EXAMPLES.md
└─ .sage-tmp/              # временные рабочие директории запусков
```

**Ключевые компоненты:**
- `AnsibleGenerator` — создаёт `inventory.ini`, `host_vars/<host>.yml`, копирует `site.yml`
- `AnsibleRunner` — запускает `ansible-playbook` с нужными флагами, пишет логи в файлы `ansible.*.stdout/stderr.log`
- `DriftDetector` — парсит `PLAY RECAP` и суммирует `changed=`

---

## Тесты

- ✅ **sysctl**  
    Довожу хосты до нужного состояния: `vm.swappiness=10`, `net.ipv4.ip_forward=1`.  
    Дрейф создавал так: `sudo sysctl -w vm.swappiness=77` → таймер/ручной `reconcile` возвращают `10`.  
    Проверка: `sysctl -n vm.swappiness` и `sysctl -n net.ipv4.ip_forward`.
    
- ✅ **/etc/hosts (managed block)**  
    Управляется только кусок между маркерами `SRE BEGIN/END`.  
    При пустых `entries` блок остаётся, но пустой (в логах видно diff).  
    Проверка: `sed -n '/SRE BEGIN/,/SRE END/p' /etc/hosts`.
    
- ✅ **Пакеты (APT)**  
    `present`: `htop`, `curl`, `nginx=1.24.*`; `absent`: `snapd`.  
    Ansible даёт `changed` только когда реально что-то ставится/удаляется.  
    Проверка: `dpkg -l | grep -E 'htop|curl|nginx'` и отсутствие `snapd`.
    
- ✅ **Docker-приложение**  
    `node-exporter` с лимитами `cpus/memory` и портом.  
    Делал `docker stop node-exporter` — на следующем прогоне контейнер снова поднимался.  
    Проверка: `docker ps | grep node-exporter`.
    
- ✅ **Мультихост на облаке**  
    Работал с `yc-ubuntu-1` и `yc-ubuntu-2`. Видел типовые SSH-проблемы (known_hosts/ключ), после фикса — всё ок.  
    Если один хост недоступен, второй всё равно приводится в порядок (для MVP — единый сервис).
    
- ✅ **Авто-reconcile по таймеру**  
    Пара `sage-reconcile.service` + `sage-reconcile.timer` крутится, на стабильной конфигурации в логах регулярно:  
    `Drift detected changed=0` и `Nothing to reconcile`.  
    Проверка: `systemctl status sage-reconcile.timer` и `journalctl -u sage-reconcile.service -n 100 -f`.







## Ограничения

- Тестировано на Ubuntu 24.04; другие дистрибутивы не проверялись
- Для Docker требуется предустановленный Docker на целевых хостах
- Контейнеры всегда приводятся к **started** (нет `state: stopped/absent`)


## Планы
- Более стабильный инсталлер
- Ротация логов
- Дрифт-детект и отчётность
- DNS

  ## Скриншоты
План оба хоста приведенные к целевой конфигурации 
![Screenshot_62](https://github.com/user-attachments/assets/93f841fb-9a54-40e0-8424-418251a1150a)
![Screenshot_63](https://github.com/user-attachments/assets/a7472654-9a75-4622-9bb9-b365981cde97)

Внесли изменения и они отобразились в плане
![План_Изменений](https://github.com/user-attachments/assets/aa6f489f-7b2d-4bf6-b877-186aa51fb0e7)
После запуска демона изменения откатились к целевой конфигурации
![Запуск демона и откат изменений](https://github.com/user-attachments/assets/d3ccfd6d-6638-424c-b0a4-8f8d5a9e866d)

## Видеодемонстрация

https://drive.google.com/file/d/12rkLPfFHDAk3BHTL8aiyBJ-H8i-sQDr6/view?usp=sharing
