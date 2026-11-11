# syntax=docker/dockerfile:1
# Wizard Duel 리눅스 전용 서버 이미지. build.sh가 compose.yaml을 거쳐 부르며, 빌드 컨텍스트는 저장소 루트다.
# 1단계는 Unity로 Main 씬을 전용 서버로 빌드하고, 2단계는 빌드 결과만 작은 이미지에 담는다.

# Unity 버전은 build.sh가 ProjectSettings/ProjectVersion.txt에서 읽어 넘긴다.
ARG UNITY_VERSION=6000.3.24f1
ARG UNITY_IMAGE_REV=3

FROM unityci/editor:ubuntu-${UNITY_VERSION}-linux-il2cpp-${UNITY_IMAGE_REV} AS builder
WORKDIR /project
COPY Packages ./Packages
COPY ProjectSettings ./ProjectSettings
COPY Assets ./Assets

# Library는 캐시 마운트에 남겨 다음 빌드에서 바뀐 에셋만 다시 가져온다.
# 라이선스는 secret으로만 받고 같은 RUN 안에서 지워 이미지 레이어에 남기지 않는다.
RUN --mount=type=secret,id=unity_license,required=true \
    --mount=type=cache,id=wizard-duel-library,target=/project/Library,sharing=locked \
    license_dir=/root/.local/share/unity3d/Unity; \
    mkdir -p "$license_dir"; \
    cp /run/secrets/unity_license "$license_dir/Unity_lic.ulf"; \
    status=0; \
    unity-editor -batchmode -nographics -logFile /dev/stdout \
        -projectPath /project \
        -buildTarget Linux64 -standaloneBuildSubtarget Server \
        -executeMethod LinuxServerBuild.Build -buildOutput /out || status=$?; \
    rm -f "$license_dir/Unity_lic.ulf"; \
    if [ "$status" -ne 0 ]; then echo "Unity build failed (exit $status)" >&2; exit "$status"; fi; \
    rm -rf /out/*_DoNotShip

FROM ubuntu:22.04
RUN useradd --create-home --uid 10001 wizard
WORKDIR /app
COPY --from=builder --chown=wizard:wizard /out/ ./
USER wizard
EXPOSE 7777/udp
# -server가 있어야 LobbyNetwork가 전용 서버로 시작한다. 로그는 표준 출력으로 보내 docker logs로 본다.
ENTRYPOINT ["./WizardDuel.x86_64", "-batchmode", "-nographics", "-server", "-logFile", "/dev/stdout"]
