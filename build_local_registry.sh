# Build the KiComLib images and push them to a registry running on this machine.
# Default: localhost:5000. When that is the target and no registry container
# is running, this script starts one:
#   docker run -d -p 5000:5000 --restart unless-stopped --name registry registry:2
# Override the registry with LOCAL_REGISTRY=host:port.
# Does not tag or push docker.io

set -eu

LOCAL_REGISTRY="${LOCAL_REGISTRY:-localhost:5000}"
FILE_DIR=$(cd "$(dirname "$0")" && pwd)
VERSION=$(tr -d '[:space:]' < "$FILE_DIR/version.txt")

echo "=== Detected software version $VERSION ==="
echo "=== Registry $LOCAL_REGISTRY ==="

# Docker Desktop resolves localhost to [::1]. If nothing is published on port
# 5000, that dial does not fail fast; it waits until it times out.
ensure_local_registry() {
  case "$LOCAL_REGISTRY" in
    localhost:5000|127.0.0.1:5000) ;;
    *) return 0 ;;
  esac

  if ! docker ps --format '{{.Names}}' | grep -qx registry; then
    if docker ps -a --format '{{.Names}}' | grep -qx registry; then
      echo "=== Start existing registry container =========================="
      docker start registry >/dev/null
    else
      echo "=== Start local registry on port 5000 =========================="
      docker run -d -p 5000:5000 --restart unless-stopped --name registry registry:2 >/dev/null
    fi
  fi

  echo "=== Wait until the registry answers =========================="
  i=0
  while [ "$i" -lt 30 ]; do
    if docker exec registry wget -q -O /dev/null http://127.0.0.1:5000/v2/; then
      return 0
    fi
    i=$((i + 1))
    sleep 1
  done
  echo "Registry on $LOCAL_REGISTRY did not become ready." >&2
  exit 1
}

ensure_local_registry

echo "=== Build kicomlib-server =========================="
docker build \
  -t "$LOCAL_REGISTRY/kicomlib/kicomlib-server:$VERSION" \
  -t "$LOCAL_REGISTRY/kicomlib/kicomlib-server:latest" \
  --progress=plain \
  -f "$FILE_DIR/Dockerfile" \
  "$FILE_DIR"

echo "=== Build kicomlib-kicad-runner =========================="
docker build \
  -t "$LOCAL_REGISTRY/kicomlib/kicomlib-kicad-runner:$VERSION" \
  -t "$LOCAL_REGISTRY/kicomlib/kicomlib-kicad-runner:latest" \
  --progress=plain \
  -f "$FILE_DIR/runner/Dockerfile" \
  "$FILE_DIR/runner"

echo "=== Push to $LOCAL_REGISTRY =========================="
docker push "$LOCAL_REGISTRY/kicomlib/kicomlib-server:$VERSION"
docker push "$LOCAL_REGISTRY/kicomlib/kicomlib-server:latest"
docker push "$LOCAL_REGISTRY/kicomlib/kicomlib-kicad-runner:$VERSION"
docker push "$LOCAL_REGISTRY/kicomlib/kicomlib-kicad-runner:latest"

echo "Build finished"
exit 0
