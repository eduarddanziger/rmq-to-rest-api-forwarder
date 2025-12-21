# Podman vs. Docker (Windows) quick developer guide

## Podman is a container engine alternative to Docker.
- Podman is daemonless and rootless, Docker not
- Podman has smaller footprint (useful in resource-constrained dev environments)
- Podman is less known than Docker
- Docker is often preinstalled on cloud CI/CD environments like GitHub Actions Containers

## 1) Podman: Build, run, and test locally from `docker-compose.yml`

### Prereqs
- Install Podman Desktop or Podman CLI (v4+) on Windows (WSL2 backend)
- Check if the `/etc/resolv.conf` file on WSL2 has valid DNS servers:
```
  PS> podman machine ssh cat /etc/resolv.conf
  nameserver 1.1.1.1
  nameserver 8.8.8.8
  search localdomain
```
- If not, create/override a resolv.conf:
```
  PS> podman machine ssh
  # sudo tee /etc/wsl.conf >/dev/null <<EOF
 [network]
 generateResolvConf = false
 EOF

  # sudo tee /etc/resolv.conf >/dev/null <<EOF
nameserver 1.1.1.1
nameserver 8.8.8.8
# (Add corporate DNS if required)
search localdomain
EOF
```
- Ensure Podman machine is running:
```
  podman machine init --now   # (first time only) or
  podman machine start
```

### Main Steps
1. ***Build*** images and ***start*** containers from repo root (where `docker-compose.yml` lives):
   - Build images: `podman compose build`
   - Start stack: `podman compose up -d`
3. ***Test***
   - List containers: `podman ps` or `podman compose ps`
   - Logs:
       - `podman logs -f rmq-to-rest-api-forwarder-forwarder-1`
       - `podman logs -f rmq-to-rest-api-forwarder-rabbitmq-1`
   - Check RabbitMQ at `http://localhost:15672/`:
     1 channel and 3 queues must exist.
4. ***Stop/clean***:
   - Stop stack: `podman compose down`
   - Optional prune: `podman system prune -a`

### Notes
- Port mappings (e.g., `8080:8080`) are accessed via `http://localhost:<host-port>`.
- Environment variables, networks, and volumes behave similarly to Docker Compose.
- If you previously installed the Python `podman-compose`, remove or ignore it to avoid confusion.

## 2) Publish to Docker Hub with Podman and test on Docker Desktop

### Tag and push from dev machine
1. Build (if not built by compose):
   - `podman build -t rmq-to-rest-api-forwarder:latest -f Projects/RmqToRestApiForwarder/Dockerfile .`
2. Log in to Docker Hub:
   - `podman login docker.io`
3. Tag for your Docker Hub repo (replace `<user>` and `<tag>`):
   - `podman tag rmq-to-rest-api-forwarder:latest docker.io/<user>/rmq-to-rest-api-forwarder:<tag>`
4. Push:
   - `podman push docker.io/<user>/rmq-to-rest-api-forwarder:<tag>`

### Use on non-dev Windows machine having Docker Desktop
- Pull:
  - `docker pull <user>/rmq-to-rest-api-forwarder:<tag>`
- Run (example ports/env):
  - `docker run -d --name rmq-forwarder -p 8080:8080 --env-file .env <user>/rmq-to-rest-api-forwarder:<tag>`
- Or with compose (if you have a compose file that references the image):
  - Ensure `image: <user>/rmq-to-rest-api-forwarder:<tag>` in `docker-compose.yml`
  - `docker compose up -d`
- Verify:
  - `docker ps`, `docker logs -f rmq-forwarder`, curl `http://localhost:8080/health`

### Tips
- Use immutable tags (version or commit SHA) to avoid confusion with `latest`.
- For private repos: `docker login` on target machine.
- Images pushed by Podman are OCI-compliant and run unmodified on Docker Desktop.

## 3) Docker: build, run, and test locally from `docker-compose.yml`

### Prereqs
- Docker Desktop (Windows/macOS) or Docker Engine (Linux) with Compose v2

### Build and run
- docker compose build
- docker compose up -d
- RabbitMQ 
  - UI for tests: http://localhost:15672, user/pass: guest/guest
  - AMQP port : 5672

### Configuration
- Forwarder reads defaults from `Projects/RmqToRestApiForwarder/appsettings.json`.
- Container overrides in `Projects/RmqToRestApiForwarder/appsettings.Docker.json`.
- Any setting can be overridden via environment variables in `docker-compose.yml`.
- To call a REST API on the host from container:
  - Set `ApiBaseUrl__Target=Local`
  - Set `ApiBaseUrl__Local=http://host.docker.internal:5027/api/AudioDevices`

### Stop/cleanup
- docker compose down

## 4) Podman: Running containers with ad hoc configuration

### Prereqs
- Step 1, 2 or 3 from above completed

### RabbitMQ
```
podman run -d --name rabbitmq `
  -p 5672:5672 -p 15672:15672 `
  -e RABBITMQ_DEFAULT_USER=guest `
  -e RABBITMQ_DEFAULT_PASS=guest `
  -e RABBITMQ_LOOPBACK_USERS=none `
  docker.io/library/rabbitmq:3.13-managementclear
```

### Forwarder
```
podman run -d --name forwarder `
  -e DOTNET_ENVIRONMENT=Docker `
  -e RabbitMQ__Service__Port=15672 `
  -e RabbitMQ__Service__QueueName=sdr_queue `
  --pod new:forwarder-pod01 `
  forwarder
```
