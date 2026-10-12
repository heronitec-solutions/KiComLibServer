# Before pushing:
# docker login

set -eu

DOCKERHUB_NAMESPACE="heronitecsolutions"
# Same default as build_local_registry.sh. Override with LOCAL_REGISTRY=host:port.
LOCAL_REGISTRY="${LOCAL_REGISTRY:-localhost:5000}"

FILE_DIR=$(cd "$(dirname "$0")" && pwd)


# get version number from code (in version.txt)
echo -e "=== Get Version Number =========================="
VERSION=$(tr -d '[:space:]' < "$FILE_DIR/version.txt")

echo -e "Detected software version $VERSION"

# server | runner | all
# GitLab jobs do not share a Docker daemon, so each job pushes only the image it built.
TARGET="${1:-all}"

# CI and a plain `docker build -t kicomlib-server:latest` leave a library tag.
# build_local_registry.sh tags ${LOCAL_REGISTRY}/kicomlib/kicomlib-server:latest instead.
resolve_local_image() {
  name="$1"
  for candidate in \
    "docker.io/library/${name}:latest" \
    "${name}:latest" \
    "${LOCAL_REGISTRY}/kicomlib/${name}:latest" \
    "${LOCAL_REGISTRY}/kicomlib/${name}:${VERSION}"
  do
    if docker image inspect "$candidate" >/dev/null 2>&1; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done
  echo "Local image ${name} is missing. Build it with ./build_local_registry.sh before pushing." >&2
  return 1
}

tag_for_hub() {
  name="$1"
  src="$(resolve_local_image "$name")"
  echo "Using local image $src"
  docker tag "$src" "docker.io/${DOCKERHUB_NAMESPACE}/${name}:${VERSION}"
  docker tag "$src" "docker.io/${DOCKERHUB_NAMESPACE}/${name}:latest"
}

push_server() {
  echo -e "=== Tag docker image =========================="

  #docker buildx inspect --bootstrap
  #docker buildx use container-builder

  #docker buildx build --platform linux/amd64,linux/arm64 -t kicomlib-server:"$VERSION" --load --no-cache --progress=plain -f Dockerfile .
  tag_for_hub kicomlib-server

  echo -e "=== Push docker image =========================="

  docker push "docker.io/${DOCKERHUB_NAMESPACE}/kicomlib-server:${VERSION}"
  docker push "docker.io/${DOCKERHUB_NAMESPACE}/kicomlib-server:latest"
}

push_runner() {
  echo -e "=== Tag kicad-runner image =========================="
  # KiCad version is pinned at build time (runner/Dockerfile):
  # org.opencontainers.image.version=10.0.6 and org.kicomlib.kicad.version=10.0.6
  # (base image kicad/kicad:10.0.6).
  tag_for_hub kicomlib-kicad-runner

  echo -e "=== Push kicad-runner image =========================="

  docker push "docker.io/${DOCKERHUB_NAMESPACE}/kicomlib-kicad-runner:${VERSION}"
  docker push "docker.io/${DOCKERHUB_NAMESPACE}/kicomlib-kicad-runner:latest"
}

case "$TARGET" in
  server) push_server ;;
  runner) push_runner ;;
  all) push_server; push_runner ;;
  *)
    echo "Usage: $0 [server|runner|all]" >&2
    exit 1
    ;;
esac

echo "Build finished"
exit 0
