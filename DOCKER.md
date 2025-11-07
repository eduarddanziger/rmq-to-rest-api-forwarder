# Docker quickstart for RmqToRestApiForwarder

## 1. Prerequisites
- Docker Desktop (Windows/macOS) or Docker Engine (Linux) with Compose v2

  - Recommended: Windows Docker extra light, without Docker Desktop UI

### 1.1 Windows Docker Extra light

PowerShell: Install WSL + Ubuntu, if not yet done.
```powershell
wsl --install -d Ubuntu
```

Ubuntu: Update packages
```sh
sudo apt update && sudo apt upgrade -y
```

Ubuntu: install Docker Engine + Compose v2
```sh
sudo apt install -y ca-certificates curl gnupg
sudo install -m0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(. /etc/os-release; echo $VERSION_CODENAME) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

Ubuntu: Add user to docker group, then re-login
```sh
sudo usermod -aG docker $USER
exit
# reopen terminal, or:
# newgrp docker
```

PowerShell: Restart WSL after editing /etc/wsl.conf
```powershell
wsl --shutdown
```

Ubuntu: Test Docker
```sh
docker version
docker run hello-world
```

## 2. Setup

Run compose from Windows terminal (example path)
```powershell
cd E:\DWP\github\rmq-to-rest-api-forwarder
wsl -d Ubuntu bash -lc "cd /mnt/e/DWP/github/rmq-to-rest-api-forwarder && docker compose up -d"
```

## 3. Build and run
- docker compose build
- docker compose up -d
- RabbitMQ UI: http://localhost:15672, user/pass: guest/guest
- AMQP port for RabbitMQ:5672

## 4. Configuration
- Forwarder reads defaults from `Projects/RmqToRestApiForwarder/appsettings.json`.
- Container overrides in `Projects/RmqToRestApiForwarder/appsettings.Docker.json`.
- Any setting can be overridden via environment variables in `docker-compose.yml`.
- To call a REST API on the host from container:
 - Set `ApiBaseUrl__Target=Local`
 - Set `ApiBaseUrl__Local=http://host.docker.internal:5027/api/AudioDevices`

## 5. Stop/cleanup
- docker compose down

