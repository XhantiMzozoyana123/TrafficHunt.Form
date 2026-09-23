# TrafficHunt

ASP.NET Core MVC app that searches YouTube for a keyword, lists the comments on the matching
videos, qualifies each comment with an LLM (Ollama) and drafts/ sends replies to the promising
ones. Storage is SQLite, so there is no database server to install or run.

| Project | Purpose |
| --- | --- |
| `TrafficHunt.Web` | MVC UI (default route is `CommentsController.Index`) |
| `TrafficHunt.Application` | DTOs and service interfaces |
| `TrafficHunt.Domain` | Entities and `ApplicationDbContext` |
| `TrafficHunt.Infrastructure` | YouTube, Ollama and Messenger implementations |
| `TrafficHunt.Maui` | Mobile client (not part of the Docker image) |

## Run with Docker

Requires Docker Engine with the Compose **v2** plugin - check with `docker compose version`
(`docker-compose` v1 cannot parse this file because it has no `version:` key).

Run everything from the **repository root**: `docker-compose.yml` builds with the repo root as
its context and `TrafficHunt.Web/Dockerfile` as the Dockerfile.

```bash
docker compose up -d --build
```

Then open <http://localhost:5000/> (or <http://localhost/> through nginx).

| Service | URL / port | Notes |
| --- | --- | --- |
| `web` | <http://localhost:5000> | the app itself, listening on 8080 inside the container |
| `nginx` | <http://localhost> (80, 443) | reverse proxy to `web:8080` (`docker/nginx/conf.d/traffichunt.conf`) |
| `ollama` | 11434 | bundled LLM server; run `docker compose exec ollama ollama pull llama3` once |
| `mysql` | 3306 | **not used by the app** (SQLite is used instead) |

The app only needs `web`, so this is the leanest way to bring it up:

```bash
docker compose up -d --build web nginx
```

Everyday commands:

```bash
docker compose ps                     # status and health
docker compose logs -f web            # startup log (EF Core migrations run automatically)
docker compose restart web
docker compose down                   # stop and remove containers, keep the database
docker compose down -v                # also delete the SQLite / mysql / ollama data volumes
```

### Configuration

`docker/.env` is loaded as the compose `env_file`, and the `environment:` block in
`docker-compose.yml` overrides it for the keys it sets. The `${VAR:-default}` placeholders are
interpolated from the **shell environment or a `.env` file in the repository root** - not from
`docker/.env`. To override anything, copy the template and edit it:

```bash
cp .env.example .env
```

| Variable | Default | Purpose |
| --- | --- | --- |
| `YOUTUBE_API_KEY` | key in `docker-compose.yml` | YouTube Data API key used to read comments |
| `LLM_URL` / `LLM_MODEL` | VPS Ollama / `llama3:latest` | LLM endpoint, e.g. `http://ollama:11434/api/generate` |
| `CONNECTION_STRING` | `Data Source=App_Data/traffichunt.db` | SQLite file, kept in the `traffichunt-web-data` volume |
| `ASPNETCORE_URLS` | `http://0.0.0.0:8080` | Kestrel binding inside the container |
| `MYSQL_*` | see `docker-compose.yml` | only relevant if you start the `mysql` service |

The database is created on first start: `Program.cs` calls `db.Database.Migrate()` and the
Dockerfile creates `/app/App_Data`, which the `traffichunt-web-data` volume persists across
rebuilds.

## Run locally without Docker

```powershell
New-Item -ItemType Directory -Force TrafficHunt.Web\App_Data   # SQLite cannot create the folder
dotnet run --project TrafficHunt.Web
```

The folder is required - if `App_Data` is missing, EF Core fails to open the SQLite file at
startup. `TrafficHunt.Web/Properties/launchSettings.json` defines the local URLs.

## Troubleshooting

- **`traffichunt-mysql` restarts forever** - the app does not use MySQL; start only what you need
  (`docker compose up -d web nginx`) or set `MYSQL_ROOT_PASSWORD` in a root `.env`.
- **`/Comments` shows no styling** - static files are served from `wwwroot`; confirm the image was
  built from a clean context (`.dockerignore` now keeps `bin/`, `obj/` and `.git/` out of it).
- **401/403 from YouTube or "quota exceeded"** - `YouTube__ApiKey` still holds the placeholder or
  the key has no quota left; set `YOUTUBE_API_KEY` in a root `.env` and `docker compose up -d`.
- **AI actions fail with "LLM:Url is not configured"** or connection errors - point `LLM_URL` at a
  reachable Ollama server (`http://ollama:11434/api/generate` for the bundled container) and make
  sure the model named by `LLM_MODEL` is pulled.
