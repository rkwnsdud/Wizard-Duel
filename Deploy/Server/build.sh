#!/usr/bin/env bash
# 서버에서 main을 받아 전용 서버 이미지를 다시 빌드하고 컨테이너를 교체한다.
# 사용법: bash Deploy/Server/build.sh
# 실제 Unity 빌드는 Dockerfile 안에서 한다. 이 스크립트는 pull → docker compose up -d --build → 시작 확인만 한다.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"
if [ -f .env ]; then set -a; . ./.env; set +a; fi

repo_root="$(git rev-parse --show-toplevel)"
branch="${DEPLOY_BRANCH:-main}"
license="${UNITY_LICENSE_FILE:-./secrets/Unity_lic.ulf}"

current="$(git -C "$repo_root" rev-parse --abbrev-ref HEAD)"
if [ "$current" != "$branch" ]; then
    echo "[build] 현재 브랜치가 '$current'입니다. '$branch'에서만 배포합니다." >&2
    exit 1
fi
if [ ! -f "$license" ]; then
    echo "[build] Unity 라이선스 파일이 없습니다: $license (Deploy/Server/README.md 참고)" >&2
    exit 1
fi

git -C "$repo_root" pull --ff-only origin "$branch"

commit="$(git -C "$repo_root" rev-parse --short HEAD)"
UNITY_VERSION="$(sed -n 's/^m_EditorVersion: *//p' "$repo_root/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
export UNITY_VERSION
# Unity 로그가 잘리지 않도록 빌드 출력을 그대로 보여 준다.
export BUILDKIT_PROGRESS="${BUILDKIT_PROGRESS:-plain}"

echo "[build] commit=$commit unity=$UNITY_VERSION"
docker compose up -d --build

# 되돌리기용으로 커밋 태그를 하나 더 붙인다(같은 이미지라 용량은 늘지 않는다).
docker tag wizard-duel-server:latest "wizard-duel-server:$commit"

echo "[build] 서버 시작을 기다립니다"
for _ in $(seq 1 30); do
    if docker compose logs --no-color server 2>/dev/null | grep -q '\[Lobby\] Listening'; then
        echo "[build] 서버가 UDP 7777에서 대기 중입니다 (commit=$commit)"
        exit 0
    fi
    sleep 2
done

echo "[build] 60초 안에 '[Lobby] Listening' 로그가 나오지 않았습니다. 마지막 로그:" >&2
docker compose logs --no-color --tail 50 server >&2
exit 1
