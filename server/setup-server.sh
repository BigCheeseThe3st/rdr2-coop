#!/bin/sh
# Sets up the RDR2 co-op relay as a background service on a Linux machine with
# systemd (a home PC, a Raspberry Pi, a cheap VPS).
#   sudo ./setup-server.sh              install (asks a couple of questions)
#   sudo ./setup-server.sh --update     put a newer relay.py in place, restart
#   sudo ./setup-server.sh --uninstall  remove it again
# Not on Linux/systemd? Just run:  python3 relay.py --password-file password.txt
set -eu
cd "$(dirname "$0")"

USER_NAME=rdr2coop
APP=/opt/rdr2coop
ETC=/etc/rdr2coop
UNIT=/etc/systemd/system/rdr2coop-relay.service

die() { echo "$*" >&2; exit 1; }
ask() { printf '%s [%s]: ' "$1" "$2" >&2; read -r a; echo "${a:-$2}"; }
yes() { printf '%s [Y/n]: ' "$1" >&2; read -r a; case "$a" in [nN]*) return 1;; *) return 0;; esac; }

[ "$(id -u)" = 0 ] || die "Run it with sudo: sudo $0 $*"
command -v systemctl >/dev/null || die "No systemd here. Run the relay directly instead: python3 relay.py --password-file password.txt"
command -v python3 >/dev/null || die "python3 is needed (3.6 or newer)"
python3 -c 'import sys; sys.exit(sys.version_info < (3, 6))' || die "python3 is too old (3.6 or newer needed)"

current_port() { sed -n 's/.*--port \([0-9]*\).*/\1/p' "$UNIT" 2>/dev/null; }

firewall_open() {   # $1 = port
  if command -v ufw >/dev/null && ufw status | grep -q "Status: active"; then
    yes "Open UDP $1 in the firewall (ufw)?" && ufw allow "$1/udp" comment "RDR2 co-op relay" >/dev/null && echo "  ufw: UDP $1 open"
  elif command -v firewall-cmd >/dev/null && firewall-cmd --state >/dev/null 2>&1; then
    yes "Open UDP $1 in the firewall (firewalld)?" && firewall-cmd -q --permanent --add-port="$1/udp" && firewall-cmd -q --reload && echo "  firewalld: UDP $1 open"
  fi
  return 0
}

firewall_close() {
  [ -n "$1" ] || return 0
  if command -v ufw >/dev/null && ufw status | grep -q "$1/udp"; then ufw delete allow "$1/udp" >/dev/null && echo "  ufw: UDP $1 closed"; fi
  if command -v firewall-cmd >/dev/null && firewall-cmd --state >/dev/null 2>&1; then
    firewall-cmd -q --permanent --remove-port="$1/udp" 2>/dev/null && firewall-cmd -q --reload && echo "  firewalld: UDP $1 closed"
  fi
  return 0
}

case "${1:-}" in
  --uninstall)
    port=$(current_port)
    systemctl disable --now rdr2coop-relay 2>/dev/null || true
    rm -f "$UNIT" && systemctl daemon-reload
    rm -rf "$APP" "$ETC"
    id "$USER_NAME" >/dev/null 2>&1 && userdel "$USER_NAME"
    firewall_close "$port"
    echo "RDR2 co-op relay removed. (A router port forward, if you made one, is still there.)"
    exit 0;;
  --update)
    [ -f "$UNIT" ] || die "Not installed yet: run sudo $0"
    install -m 644 -o root -g root relay.py "$APP/relay.py"
    systemctl restart rdr2coop-relay
    sleep 1; systemctl is-active --quiet rdr2coop-relay || die "The relay didn't start: journalctl -u rdr2coop-relay"
    echo "Updated: $(journalctl -u rdr2coop-relay -n 1 -o cat)"
    exit 0;;
  "") ;;
  *) die "usage: sudo $0 [--update|--uninstall]";;
esac

echo "RDR2 co-op relay setup"
cur=$(current_port || true)
port=$(ask "UDP port players connect to" "${cur:-27460}")
case "$port" in *[!0-9]*|"") die "Not a port number: $port";; esac
[ "$port" -ge 1024 ] && [ "$port" -le 65535 ] || die "Use a port from 1024 to 65535"

if [ -s "$ETC/password" ] && yes "Keep the existing room password?"; then
  password=$(cat "$ETC/password")
elif yes "Make up a strong random room password for you?"; then
  password=$(python3 -c 'import secrets; print(secrets.token_urlsafe(12))')
else
  while :; do
    printf 'Room password: ' >&2; stty -echo 2>/dev/null; read -r password; stty echo 2>/dev/null; echo >&2
    printf 'Again: ' >&2; stty -echo 2>/dev/null; read -r again; stty echo 2>/dev/null; echo >&2
    [ -n "$password" ] && [ "$password" = "$again" ] && break
    echo "  The two didn't match (or were empty); try again." >&2
  done
fi

id "$USER_NAME" >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin "$USER_NAME"
install -d -m 755 -o root -g root "$APP"
install -m 644 -o root -g root relay.py "$APP/relay.py"
install -d -m 750 -o root -g "$USER_NAME" "$ETC"
( umask 027; printf '%s' "$password" > "$ETC/password" )
chown root:"$USER_NAME" "$ETC/password"; chmod 640 "$ETC/password"
sed "s/@PORT@/$port/g" rdr2coop-relay.service > "$UNIT"
systemctl daemon-reload
systemctl enable --quiet rdr2coop-relay
systemctl restart rdr2coop-relay
sleep 1
systemctl is-active --quiet rdr2coop-relay || die "The relay didn't start: journalctl -u rdr2coop-relay"
firewall_open "$port"

lan=$(hostname -I 2>/dev/null | awk '{print $1}')
cat <<EOF

The relay is running (it starts again by itself after a reboot).

Tell your players:
  Server:   this machine's address (see below)
  Port:     $port
  Password: $password

Players on your own network: use ${lan:-this machine's local IP}.
Players over the internet:
  1. In your router, forward UDP port $port to ${lan:-this machine's local IP}.
  2. Give them your public IP address, or better a free dynamic DNS name (e.g. from
     duckdns.org or noip.com) that follows your IP when it changes.
  A VPS needs no forwarding: just its firewall open for UDP $port.

Logs:      journalctl -u rdr2coop-relay -f
Password:  $ETC/password (change it, then: sudo systemctl restart rdr2coop-relay)
EOF
