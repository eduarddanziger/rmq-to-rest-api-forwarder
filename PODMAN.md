# Podman (Windows) quick guide

## 1) Build, run, and test locally from `docker-compose.yml`

Prereqs
- Podman Desktop or Podman CLI (v4+) on Windows (WSL2 backend) installed

Steps
1. Ensure Podman machine is running:
   - `podman machine init --now` (first time only) or `podman machine start`
2. Start containers from repo root (where `docker-compose.yml` lives):
   - Build images: `podman compose build`
   - Start stack: `podman compose up -d`
3. Tests
   - List containers: `podman ps` or `podman compose ps`
   - Logs:
       - `podman logs -f rmq-to-rest-api-forwarder-forwarder-1`
       - `podman logs -f rmq-to-rest-api-forwarder-rabbitmq-1`
   - Check RabbitMQ  `http://localhost:15672/`:
   1 channel and 3 Queues must exist.
4. Stop/clean:
   - Stop stack: `podman compose down`
   - Optional prune: `podman system prune -a`

Notes
- Port mappings (e.g. `8080:8080`) are accessed via `http://localhost:<host-port>`.
- Environment variables, networks, and volumes behave similarly to Docker Compose.
- If you previously installed the Python `podman-compose`, remove or ignore it to avoid confusion.

## 2) Publish to Docker Hub with Podman and test on Docker Desktop

Tag and push from dev machine
1. Build (if not built by compose):
   - `podman build -t rmq-to-rest-api-forwarder:latest -f Projects/RmqToRestApiForwarder/Dockerfile .`
2. Log in to Docker Hub:
   - `podman login docker.io`
3. Tag for your Docker Hub repo (replace `<user>` and `<tag>`):
   - `podman tag rmq-to-rest-api-forwarder:latest docker.io/<user>/rmq-to-rest-api-forwarder:<tag>`
4. Push:
   - `podman push docker.io/<user>/rmq-to-rest-api-forwarder:<tag>`

Use on non-dev Windows machine (Docker Desktop)
- Pull:
  - `docker pull <user>/rmq-to-rest-api-forwarder:<tag>`
- Run (example ports/env):
  - `docker run -d --name rmq-forwarder -p 8080:8080 --env-file .env <user>/rmq-to-rest-api-forwarder:<tag>`
- Or with compose (if you have a compose file that references the image):
  - Ensure `image: <user>/rmq-to-rest-api-forwarder:<tag>` in `docker-compose.yml`
  - `docker compose up -d`
- Verify:
  - `docker ps`, `docker logs -f rmq-forwarder`, curl `http://localhost:8080/health`

Tips
- Use immutable tags (version, commit SHA) to avoid confusion with `latest`.
- For private repos: `docker login` on target machine.
- Images pushed by Podman are OCI-compliant and run unmodified on Docker Desktop.
