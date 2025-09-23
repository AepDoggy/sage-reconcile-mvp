#!/bin/bash
set -euo pipefail

# === Конфиг из окружения (с разумными дефолтами) ===
CONFIG="${CONFIG:-examples/config.yaml}"                 # путь к вашему YAML
OVERRIDE_KEY_PATH="${OVERRIDE_KEY_PATH:-}"              # если нужно переписать private_key_path в конфиге
DISABLE_HOST_KEY_CHECKING="${DISABLE_HOST_KEY_CHECKING:-false}"
FORKS="${FORKS:-5}"
LIMIT="${LIMIT:-}"                                      # опционально: --limit для сервиса

# === Абсолютные пути к репо и файлам ===
ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ "$CONFIG" != /* ]]; then CONFIG="$ROOT_DIR/$CONFIG"; fi
DIST_DIR="$ROOT_DIR/dist/sage"

echo "==> Checks"
command -v dotnet >/dev/null || { echo "dotnet not found"; exit 1; }
command -v ansible-playbook >/dev/null || { echo "ansible not found"; exit 1; }

echo "==> Publish CLI to $DIST_DIR (ROOT_DIR=$ROOT_DIR)"
rm -rf "$DIST_DIR"; mkdir -p "$DIST_DIR"

echo "==> Deploy to /opt/sage"
sudo mkdir -p /opt/sage/ansible_templates /opt/sage/examples
sudo cp -r $DIST_DIR/ /opt/sage/
sudo cp -r $ROOT_DIR/ansible_templates/* /opt/sage/ansible_templates/
sudo cp $CONFIG /opt/sage/examples/config.yaml
sudo cp -r $ROOT_DIR/src/SageCli/bin/Debug/net8.0/* /opt/sage/

# Перезаписать ключ, если задан
if [[ -n "$OVERRIDE_KEY_PATH" ]]; then
  sudo sed -i "s#private_key_path:.*#private_key_path: ${OVERRIDE_KEY_PATH}#" /opt/sage/examples/config.yaml
fi

# Извлечь адреса хостов из YAML и добавить в known_hosts для root
echo "==> known_hosts"
HOSTS=$(awk '
  /^hosts:/{inhosts=1; next}
  inhosts && /^[^[:space:]-]/{inhosts=0}
  inhosts && /address:[[:space:]]*/{print $2}
' "$CONFIG")
if [[ -n "${HOSTS:-}" ]]; then
  sudo mkdir -p /root/.ssh
  for ip in $HOSTS; do sudo ssh-keyscan -H "$ip" || true; done | sudo tee -a /root/.ssh/known_hosts >/dev/null
  sudo chmod 644 /root/.ssh/known_hosts
fi

# Опционально отключить проверку ключей в ansible
HOST_KEY_ENV=""
if [[ "$DISABLE_HOST_KEY_CHECKING" == "true" ]]; then
  HOST_KEY_ENV='Environment=ANSIBLE_HOST_KEY_CHECKING=False'
fi

# Доборка для --limit
EXTRA_LIMIT=""
if [[ -n "$LIMIT" ]]; then
  EXTRA_LIMIT=" --limit $LIMIT"
fi

echo "==> Install systemd unit + timer"
# service
sudo tee /etc/systemd/system/sage-reconcile.service >/dev/null <<EOF
[Unit]
Description=Sage Reconcile (plan->apply if drift)
After=network-online.target
Wants=network-online.target

[Service]
Type=oneshot
WorkingDirectory=/opt/sage
Environment=PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin
$HOST_KEY_ENV
ExecStart=/usr/bin/dotnet /opt/sage/SageCli.dll reconcile -f /opt/sage/examples/config.yaml --forks $FORKS$EXTRA_LIMIT
EOF

# timer
sudo tee /etc/systemd/system/sage-reconcile.timer >/dev/null <<'EOF'
[Unit]
Description=Run Sage Reconcile every 5 minutes

[Timer]
OnBootSec=2min
OnUnitActiveSec=5min
AccuracySec=30s
Unit=sage-reconcile.service

[Install]
WantedBy=timers.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable --now sage-reconcile.timer

echo "==> Done.
- Проверка таймера:   systemctl status sage-reconcile.timer
- Ручной прогон:      sudo systemctl start sage-reconcile.service
- Логи сервиса:       journalctl -u sage-reconcile.service -n 100 -f"

