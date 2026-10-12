# KiCad patch tag pinned to 10.0.6 (newest 10.0.x published on Docker Hub
# as of 2026-09-27). Do not float this to kicad/kicad:10.0 or :latest.
# The official image already creates a non-root user "kicad" (uid 1000).
FROM kicad/kicad:10.0.6 AS runtime

LABEL org.opencontainers.image.title="kicomlib-kicad-runner" \
      org.opencontainers.image.version="10.0.6" \
      org.opencontainers.image.description="KiComLib kicad-runner pinned to KiCad 10.0.6" \
      org.kicomlib.kicad.version="10.0.6"

USER root
RUN mkdir -p /work /opt/kicomlib-runner \
    && chown -R kicad:kicad /work /opt/kicomlib-runner \
    && python3 --version \
    && kicad-cli version

COPY --chown=kicad:kicad app /opt/kicomlib-runner/app

ENV PYTHONPATH=/opt/kicomlib-runner \
    PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    RUNNER_WORK_DIR=/work \
    TMPDIR=/work \
    TEMP=/work \
    TMP=/work \
    RUNNER_PORT=8080

USER kicad
WORKDIR /work
EXPOSE 8080
CMD ["python3", "-m", "app.server"]

# pytest is not part of the runtime image. Build with --target test to run it.
FROM runtime AS test
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3-pytest \
    && rm -rf /var/lib/apt/lists/*
COPY --chown=kicad:kicad tests /opt/kicomlib-runner/tests
COPY --chown=kicad:kicad fixtures /opt/kicomlib-runner/fixtures
USER kicad
WORKDIR /opt/kicomlib-runner

# Default image is the runtime stage (no pytest).
FROM runtime
