# Hosting Guide — rpg.dionysus.dk

The RPG web app runs **locally on your Windows PC** and is exposed to the internet via an **SSH reverse tunnel** through a Google Cloud server. Players access it at `https://rpg.dionysus.dk`.

---

## Tech Stack

```
Player (browser)
    │  HTTPS (443)
    ▼
Google Cloud VM — dionysus-server (35.228.204.127)
    Apache 2.4 + Let's Encrypt SSL
    │  Reverse proxy → localhost:5100
    ▼
SSH Reverse Tunnel  ◄────────────────────────────────────
    (GCP port 5100 forwards through SSH to Windows PC)     │
    ▼                                                       │
Windows PC (your machine)                             ssh.exe keeps
    RPGWeb — Blazor Server on port 5100              tunnel open
    Ollama  — LLM inference on port 11434
```

| Layer | Technology | Where it runs |
|-------|-----------|---------------|
| Frontend/Backend | Blazor Server (.NET 8) | Your Windows PC |
| LLM inference | Ollama (`granite4:3b`) | Your Windows PC |
| Tunnel | SSH reverse tunnel (`-R`) | Your Windows PC → GCP |
| Web server / SSL | Apache 2.4 + Let's Encrypt | GCP Ubuntu 20.04 |
| Domain | `rpg.dionysus.dk` A record → 35.228.204.127 | DNS registrar |

---

## Starting the Server

Configure the tunnel target and private-key path in the current PowerShell session. These values are deliberately not stored in the repository:

```powershell
$env:RPGWEB_SSH_KEY_PATH = "C:\path\to\your\private-key"
$env:RPGWEB_SSH_SERVER = "user@server.example.com"
```

Then start the local server and tunnel:

Run this **one script** from the project root — it does everything:

```powershell
cd C:\Devstuff\git\CSharpRPGBackend
.\start-tunnel.ps1
```

What it does:
1. **Builds** RPGWeb
2. **Starts RPGWeb** on `http://127.0.0.1:5100` as a background job
3. **Opens an SSH tunnel** — GCP port 5100 → your PC port 5100
4. **Auto-reconnects** if the tunnel drops (every 5 seconds)
5. **Ctrl+C** shuts down both RPGWeb and the tunnel cleanly

**Ollama must also be running** for LLM features to work:
```powershell
ollama serve
```

Players can then connect at: **https://rpg.dionysus.dk**

---

## Prerequisites

| Requirement | Location |
|-------------|----------|
| SSH private key | Set `RPGWEB_SSH_KEY_PATH` or pass `-SshKeyPath` |
| SSH server | Set `RPGWEB_SSH_SERVER` or pass `-SshServer` in `user@host` form |
| .NET 8 SDK | Must be installed on this PC |
| Ollama | Must be running on this PC |

---

## How the Tunnel Works

The SSH reverse tunnel (`-R`) tells the GCP server to listen on port 5100 and forward any incoming connections back through the SSH connection to `127.0.0.1:5100` on your Windows PC.

```
-R 5100:127.0.0.1:5100
      │       └── connect to this on the Windows PC
      └── listen on this port on the GCP server
```

> **Important:** The tunnel uses `127.0.0.1` not `localhost`. On Windows,
> `localhost` can resolve to `::1` (IPv6) first, but RPGWeb listens on
> IPv4 loopback (`127.0.0.1`), which would silently drop those connections.

Apache on GCP proxies `https://rpg.dionysus.dk` → `http://localhost:5100` with WebSocket support enabled (required for Blazor SignalR).

---

## GCP Server Configuration

### Apache virtual host
`/etc/apache2/sites-enabled/rpg.dionysus.dk-le-ssl.conf`

Key settings:
- `ProxyPass / http://localhost:5100/ disablereuse=On` — proxies to the tunnel; `disablereuse=On` prevents stale connection errors
- `RewriteRule` with `ws://` — WebSocket upgrade for Blazor SignalR
- `ProxyTimeout 300` — allows long-running LLM responses

### SSL Certificate
Managed by Let's Encrypt / Certbot and configured for automatic renewal.

Manual renewal if needed:
```powershell
ssh -i $env:RPGWEB_SSH_KEY_PATH $env:RPGWEB_SSH_SERVER `
  "sudo certbot renew && sudo systemctl reload apache2"
```

---

## Connecting to the GCP Server

```powershell
# SSH
ssh -i $env:RPGWEB_SSH_KEY_PATH $env:RPGWEB_SSH_SERVER

# SCP (copy files to server)
scp -i $env:RPGWEB_SSH_KEY_PATH myfile.txt "${env:RPGWEB_SSH_SERVER}:~/"
```

---

## Troubleshooting

| Problem | Likely cause | Fix |
|---------|-------------|-----|
| Proxy Error on site | Tunnel not running | Run `.\start-tunnel.ps1` |
| Site loads but LLM fails | Ollama not running | Run `ollama serve` |
| Tunnel drops repeatedly | SSH keepalive | Script auto-reconnects; check internet |
| SSL cert expired | Let's Encrypt renewal | Run certbot renew (see above) |
| Empty reply through tunnel | IPv6/IPv4 mismatch | Ensure tunnel uses `127.0.0.1` not `localhost` |
