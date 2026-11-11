# 리눅스 전용 서버 (Docker)

리눅스 서버에서 저장소를 받아 `bash Deploy/Server/build.sh` 한 번으로 전용 서버를 빌드하고 실행한다.

- `build.sh`: 현재 브랜치가 `main`인지와 Unity secret 파일 세 개를 확인한 뒤 `git pull --ff-only`, `docker compose up -d --build`를 실행하고, 로그에 `[Lobby] Listening`이 나올 때까지 최대 60초 기다린다.
- `Dockerfile`: 1단계에서 `unityci/editor` 이미지로 `unity-build.sh`를 실행해 `Assets/Scenes/Main.unity`를 Linux Dedicated Server로 빌드한다(`LinuxServerBuild.Build`). 2단계에서는 빌드 결과만 `ubuntu:22.04`에 담아 `-server`로 실행한다.
- `unity-build.sh`: 빌드 컨테이너 안에서 Unity 라이선스를 활성화하고, 빌드한 뒤, 라이선스를 반납한다. 빌드가 실패해도 반납은 한다.
- `compose.yaml`: UDP 7777을 열고, 서버가 죽으면 다시 띄운다(`restart: unless-stopped`).
- `Dockerfile.dockerignore`: 빌드 컨텍스트로 `Assets`, `Packages`, `ProjectSettings`와 `unity-build.sh`만 보낸다.

## 서버 준비 (처음 한 번)

1. **사양**: x86_64, RAM 8GB 이상, 디스크 여유 20GB 이상을 권장한다. Unity 에디터 이미지만 5.7GB이고, 첫 빌드에서 에셋을 전부 가져온다.
2. **Docker**: Docker Engine 23 이상(BuildKit 기본 사용)과 `docker compose` 플러그인을 설치하고, 배포할 계정을 `docker` 그룹에 넣는다.
3. **포트**: 방화벽(ufw 등)과 클라우드 보안 그룹에서 **UDP** 7777을 연다.
4. **저장소 받기**: 서버에서 SSH 키를 만들어 본인 GitHub 계정의 SSH keys에 등록하고 clone한다. 저장소 전용 deploy key는 저장소 관리자만 등록할 수 있어서 개인 키를 쓴다. 이 키로는 본인 계정이 접근할 수 있는 모든 저장소에 접근할 수 있다.
   ```bash
   ssh-keygen -t ed25519 -C wizard-duel-server
   cat ~/.ssh/id_ed25519.pub
   git clone git@github.com:rkwnsdud/Wizard-Duel.git
   ```
5. **Unity 라이선스와 계정**: 빌드 컨테이너가 Unity 라이선스를 직접 활성화한다. `.ulf` 파일을 그대로 넣으면 Unity가 라이선스로 인식하지 못하고 종료 코드 198("No valid Unity Editor license")로 끝난다. 그래서 GameCI와 같은 방식으로 `.ulf`에서 시리얼을 꺼내 Unity 계정 이메일·비밀번호와 함께 활성화하고, 빌드가 끝나면 반납한다. 아래 세 파일을 `Deploy/Server/secrets/`에 둔다. 이 폴더는 `.gitignore`에 들어 있고, 빌드 중에만 secret으로 컨테이너에 들어가 이미지에는 남지 않는다.

   | 파일 | 내용 |
   |---|---|
   | `Unity_lic.ulf` | PC의 `C:\ProgramData\Unity\Unity_lic.ulf`. 시리얼을 꺼내는 데만 쓴다. |
   | `unity_email` | 라이선스가 등록된 Unity 계정 이메일 |
   | `unity_password` | 그 계정의 비밀번호 |

   서버에서 폴더를 만든다.
   ```bash
   mkdir -p ~/Wizard-Duel/Deploy/Server/secrets
   ```
   PC에서 라이선스 파일을 보낸다(`<계정>@<서버>`는 실제 접속 정보로 바꾼다).
   ```bash
   scp "C:/ProgramData/Unity/Unity_lic.ulf" <계정>@<서버>:~/Wizard-Duel/Deploy/Server/secrets/Unity_lic.ulf
   ```
   서버에서 이메일과 비밀번호를 파일로 만든다. 비밀번호가 셸 기록에 남지 않도록 입력받아 쓴다.
   ```bash
   cd ~/Wizard-Duel/Deploy/Server/secrets && IFS= read -rp 'Unity email: ' v && printf '%s' "$v" > unity_email && IFS= read -rsp 'Unity password: ' v && printf '%s' "$v" > unity_password && unset v && echo
   ```
   서버에서 권한을 줄인다.
   ```bash
   chmod 600 ~/Wizard-Duel/Deploy/Server/secrets/*
   ```
   - 계정은 https://id.unity.com 에 이메일·비밀번호로 직접 로그인되는 계정이어야 한다. Google·Apple 등 소셜 로그인으로 만든 계정은 비밀번호가 없어 `Failed to login`(HTTP 401)이 나므로, 계정 설정에서 비밀번호를 먼저 만든다.
   - 2단계 인증이 켜진 계정은 명령줄 활성화가 실패할 수 있다.
   - 로그인에 실패하면 계정이 잠기지 않도록 다시 시도하지 않고 바로 멈추며, 이메일 앞 두 글자·도메인과 비밀번호 길이를 보여 준다. 파일 내용이 의도와 같은지 이것으로 확인한다.
   - 컨테이너마다 새 기기로 활성화되므로 `unity-build.sh`가 빌드 뒤 `-returnlicense`로 반납한다. 빌드를 강제로 끊어 반납되지 않았다면 Unity 계정 페이지에서 활성화된 기기를 해제한다.
   - 활성화·반납 로그는 비밀번호와 시리얼을 가리고 관련 줄만 보여 준다.

## 실행

```bash
bash Deploy/Server/build.sh
```

- 처음 빌드는 에셋을 전부 가져오느라 오래 걸린다. 이후에는 `Library`를 BuildKit 캐시 마운트에 남겨 바뀐 것만 다시 가져온다. `docker builder prune`을 하면 이 캐시가 지워져 다음 빌드가 다시 오래 걸린다.
- `Assets`, `Packages`, `ProjectSettings`가 그대로면 Unity 빌드를 건너뛰고 컨테이너도 다시 띄우지 않는다. 문서만 바뀐 pull이 여기에 해당한다.
- 빌드가 실패하면 기존 컨테이너가 그대로 돌아간다.
- 로그 보기: `docker compose -f Deploy/Server/compose.yaml logs -f`
- 클라이언트 접속: `WizardDuel.exe -join <서버 주소>`. 도메인도 된다(`-join limeskin.kr`). 에디터에서는 **Tools > Wizard Duel > Network Test** 창(Mode: Main)에서 **Build** 후 **Remote Client (limeskin.kr)**를 누른다. 클라이언트도 서버와 같은 커밋으로 빌드해야 한다(프로토콜 버전·맵 지문이 맞아야 한다).

`Deploy/Server/.env`로 기본값을 바꿀 수 있다(git에 올라가지 않는다).

| 변수 | 기본값 | 설명 |
|---|---|---|
| `DEPLOY_BRANCH` | `main` | 배포할 브랜치 |
| `UNITY_LICENSE_FILE` | `./secrets/Unity_lic.ulf` | 라이선스 파일 경로(`Deploy/Server` 기준) |

## 주의

- 새 이미지로 교체하면 진행 중인 방이 모두 끊긴다.
- 되돌리기: 빌드마다 `wizard-duel-server:<커밋>` 태그가 붙는다. `docker tag wizard-duel-server:<커밋> wizard-duel-server:latest` 후 `docker compose -f Deploy/Server/compose.yaml up -d`로 이전 버전을 띄운다. 오래된 태그는 `docker image rm`으로 지운다.
- 주기적으로 자동 배포하려면 cron에 등록한다. `flock`으로 빌드가 겹치지 않게 한다.
  ```bash
  */10 * * * * flock -n /tmp/wizard-duel-build.lock bash $HOME/Wizard-Duel/Deploy/Server/build.sh >> $HOME/wizard-duel-build.log 2>&1
  ```
