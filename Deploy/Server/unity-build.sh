#!/usr/bin/env bash
# Dockerfile의 builder 단계에서 실행한다: Unity 라이선스 활성화 → 전용 서버 빌드 → 라이선스 반납.
# .ulf 파일을 그대로 넣으면 컨테이너의 Unity가 라이선스로 인식하지 못한다(종료 코드 198, "No valid Unity Editor license").
# GameCI(unity-builder v4)와 같은 방식으로 .ulf에서 시리얼을 꺼내 Unity 계정 이메일·비밀번호와 함께 활성화한다.
# 사용법: bash unity-build.sh <출력 폴더>
set -uo pipefail

output="${1:-/out}"
secrets="${UNITY_SECRETS_DIR:-/run/secrets}"
work="$(mktemp -d)"
blank="$work/BlankProject"
mkdir -p "$blank/Assets"

read_secret() { tr -d '\r\n' < "$secrets/$1"; }
email="$(read_secret unity_email)"
password="$(read_secret unity_password)"
# DeveloperData는 base64이고, 디코딩한 앞 4바이트를 버리면 시리얼(XX-XXXX-XXXX-XXXX-XXXX-XXXX)이 남는다.
serial="$(read_secret unity_license | sed -n 's/.*<DeveloperData Value="\([^"]*\)".*/\1/p' | base64 -d 2>/dev/null | tail -c +5)"

if [ -z "$email" ] || [ -z "$password" ]; then
    echo "[unity] Unity 계정 이메일 또는 비밀번호 secret이 비어 있습니다." >&2
    exit 1
fi
if [ -z "$serial" ]; then
    echo "[unity] 라이선스 파일에서 시리얼(DeveloperData)을 찾지 못했습니다." >&2
    exit 1
fi

# 활성화·반납 로그에는 비밀번호와 시리얼이 찍힐 수 있어 파일로 받고, 관련 줄만 가려서 보여 준다.
show_log() {
    [ -f "$1" ] || return 0
    grep -i -E "licens|entitlement|error|fail" "$1" | grep -v -F -e "$password" -e "$serial" -e "$email" | tail -n 40 >&2
}

# unity-editor는 GameCI 이미지의 래퍼로 xvfb와 -batchmode를 붙여 Unity를 실행한다.
activate() {
    local attempt
    for attempt in 1 2 3; do
        if unity-editor -quit -logFile "$work/activate.log" \
            -serial "$serial" -username "$email" -password "$password" -projectPath "$blank"; then
            echo "[unity] 라이선스 활성화 완료"
            return 0
        fi
        echo "[unity] 라이선스 활성화 실패 ($attempt/3)" >&2
        show_log "$work/activate.log"
        if [ "$attempt" -lt 3 ]; then sleep $((attempt * 15)); fi
    done
    return 1
}

# 컨테이너마다 새 기기로 활성화되므로 빌드가 끝나면 반드시 반납해 활성화 수가 쌓이지 않게 한다.
return_license() {
    if unity-editor -quit -logFile "$work/return.log" \
        -returnlicense -username "$email" -password "$password" -projectPath "$blank"; then
        echo "[unity] 라이선스 반납 완료"
    else
        echo "[unity] 라이선스 반납 실패. Unity 계정의 활성화 기기 목록을 확인하세요." >&2
        show_log "$work/return.log"
    fi
}

cleanup() {
    return_license
    rm -rf "$work"
}

if ! activate; then
    echo "[unity] 라이선스를 활성화하지 못했습니다. 이메일·비밀번호와 계정의 2단계 인증 설정을 확인하세요." >&2
    rm -rf "$work"
    exit 1
fi
trap cleanup EXIT

# 빌드 로그는 그대로 보여 주되, 혹시 찍히는 시리얼(마지막 4자리 제외 부분)과 이메일은 가린다.
unity-editor -logFile /dev/stdout \
    -projectPath /project \
    -buildTarget Linux64 -standaloneBuildSubtarget Server \
    -executeMethod LinuxServerBuild.Build -buildOutput "$output" \
    | sed -u -e "s|${serial%????}|[serial]|g" -e "s|${email}|[email]|g"
status=${PIPESTATUS[0]}
if [ "$status" -ne 0 ]; then
    echo "[unity] 서버 빌드 실패 (exit $status)" >&2
    exit "$status"
fi

rm -rf "$output"/*_DoNotShip
echo "[unity] 서버 빌드 완료: $output"
