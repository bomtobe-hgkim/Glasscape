# Glasscape

Windows desktop personalization with taskbar transparency, auto-hide and local video wallpapers. The app supports **English and Korean**, saves settings locally, and includes two original CC0 scenes: Aurora Flow and Ocean Drift.

[Source](https://github.com/bomtobe-hgkim/Glasscape) · [Privacy policy](PRIVACY.md) · [Support](SUPPORT.md) · [Security reports](SECURITY.md) · [Publication review](SECURITY_REVIEW.md) · [License scope](LICENSE.md)

Support / 고객지원: **bomtobe.sw@gmail.com**

C#으로 만든 Windows 데스크톱 설정 앱입니다. **작업표시줄 투명화·자동 숨김·동영상 배경화면·로그인 시 자동 실행**을 한곳에서 설정합니다. 원본 TranslucentTB나 Lively 앱을 별도로 설치할 필요는 없습니다.

설정에서 **시스템 언어·English·한국어**를 선택할 수 있습니다. 공개 소스의 독립 작성 부분은 MIT, 지정된 통합 코드와 통합 배포본은 GPL 조건을 따릅니다. 자세한 범위는 [LICENSE.md](LICENSE.md)에 있습니다.

## 실행과 설치

`dist/Glasscape-win-x64.zip`을 압축 해제한 뒤 `Glasscape.exe`를 **일반 권한**으로 실행하세요. 실행 파일과 프로젝트 이름도 Glasscape로 통일했습니다. Windows 10/11 x64용이며 배포본에는 .NET 런타임이 포함됩니다. DLL과 `mpv`, `samples` 폴더도 함께 유지하세요.

포터블 버전을 안정적인 위치에 설치하려면 `Install-Glasscape.cmd`를 실행하거나 설정 화면의 **이 PC에 설치**를 누르세요. 사용자 프로그램 폴더 `%LOCALAPPDATA%/Programs/Glasscape/<버전>`에 복사하고 시작 메뉴에 바로가기를 만듭니다. 설치 화면에서 **Windows 로그인 시 자동으로 실행** 여부를 선택할 수 있습니다. 관리자 권한은 필요 없습니다. Microsoft Store 버전은 Windows의 패키지 설치·업데이트와 시작 작업을 사용하며 이 별도 복사 설치 버튼을 표시하지 않습니다.

설치 없이 사용할 때도 설정의 **Windows와 함께 시작**을 켜면 다음 로그인부터 조용히 실행하고 저장한 배경화면을 복원합니다. 켜지 않으면 매번 직접 실행해야 합니다. 시작 항목은 이 앱의 HKCU Run 값만 변경하며, 명령은 실행 파일의 전체 경로와 `--background`로 등록됩니다. 포터블 폴더를 옮겼다면 새 위치에서 시작 옵션을 다시 설정하세요.

창을 닫으면 트레이에서 계속 실행됩니다. 트레이를 두 번 클릭하거나 다시 실행하면 설정 창을 엽니다. 완전히 종료하려면 트레이 메뉴의 **종료**를 선택하세요.

## 작업표시줄

- **투명화**를 켜고 배경 불투명도를 0–100%로 조절합니다. 0%는 완전 투명입니다.
- **자동 숨김**은 Windows 작업표시줄의 실제 설정을 변경합니다. 투명화와 함께 사용할 수 있고 선택한 상태는 저장됩니다.
- 투명화를 끄거나 앱을 종료하면 원래 배경으로 돌아옵니다. 자동 숨김은 사용자가 선택한 Windows 설정으로 유지됩니다.
- 여러 모니터에 적용하며 Explorer 재시작 후 다시 연결합니다. 원본 TranslucentTB가 실행 중이면 먼저 종료하세요.

## 동영상 배경화면

**내 배경화면**에 **오로라의 흐름**, **느린 바다** 두 개의 기본 영상이 들어 있습니다. 영상과 썸네일이 배포본의 `samples` 폴더에 포함되어 바로 골라 적용할 수 있습니다. 라이브러리에서 제거한 기본 영상은 다음 실행 때 다시 추가하지 않으며, 기존에 가져온 영상과 선택한 배경화면은 유지합니다.

오로라의 흐름과 느린 바다는 이 프로젝트에서 직접 만든 CC0 동영상으로, 720p·30fps·8초·무음 반복 영상입니다. 각 영상의 정보는 `samples/README.md`에 기록했습니다. rain, rain2, town은 기본 라이브러리와 실행 배포본·소스 ZIP에서 제외하며, 개발 폴더의 원본은 보존합니다.

**동영상 가져오기** 또는 파일 끌어놓기로 MP4, MKV, WebM, MOV, AVI, WMV 파일을 추가합니다. 원본 파일을 복사·이동·수정하지 않으므로 파일 위치는 유지하세요. 파일이 없거나 재생할 수 없으면 화면에서 이유를 안내합니다.

- 주 모니터, 선택한 모니터 또는 모든 모니터에 적용합니다.
- 화면 채우기/맞추기, 무음/음량, 일시정지/재생/중지를 설정합니다.
- 전체 화면 앱, 배터리, 잠금/절전 상태에서는 정책에 따라 멈춥니다. 사용자가 누른 일시정지는 다른 자동 조건이 해제되어도 유지합니다.
- 바탕화면 아이콘 뒤에서 재생하며 기존 정적 배경은 바꾸지 않습니다. 중지/종료하면 기존 배경이 다시 나타납니다.
- 종료 시 소유한 재생기만 정리합니다. 재생기는 별도 프로세스와 종료 보호 Job Object를 사용합니다.

설정은 `%LOCALAPPDATA%/Glasscape/settings.json`에 원자적으로 저장됩니다. 새 폴더에 설정이 없으면 이전 앱의 설정을 읽어와 원본과 개인 영상 경로를 보존합니다. 잘못된 JSON은 원본을 보존하고 안전한 기본 설정으로 복구합니다. 로그는 같은 폴더의 `app.log`에 저장됩니다. 기존 자동 시작 항목은 읽기만으로 변경하지 않으며, 설치하거나 자동 시작을 켜거나 끌 때 같은 앱의 설치 경로로 확인된 이전 항목을 정리합니다.

## HTML 시안과 아이콘

`design/index.html`을 브라우저에서 열면 처음 제작한 인터랙티브 디자인 시안을 볼 수 있습니다. 이 HTML은 미리보기이며 Windows 설정을 변경하지 않습니다. 네이티브 C# 화면은 같은 색상과 구조를 적용했습니다. HTML 문법/조작 검사를 통과했으며, 브라우저 도구의 로컬 URL 정책 때문에 자동 브라우저 화면 검증은 수행하지 않았습니다.

이미지 생성으로 제작한 아이콘은 `assets/app-icon.png`, Windows 다중 해상도 아이콘은 `assets/app.ico`입니다. 생성 프롬프트는 `assets/icon-prompt.txt`에 기록했습니다.

## 빌드와 검증

개발 시 Windows와 .NET 10 SDK가 필요합니다. `Glasscape.slnx`를 Visual Studio에서도 열 수 있습니다.

공개 저장소에는 외부 네이티브 실행 파일과 다운로드 아카이브가 포함되지 않습니다. 처음 빌드하기 전에 해시가 고정된 의존성을 복원하세요:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/fetch-native.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/fetch-mpv.ps1
```

```powershell
dotnet build Glasscape.slnx -c Release
dotnet run --project src/Glasscape -c Release
dotnet run --project tests/Glasscape.Tests -c Release
dotnet run --project tests/Glasscape.Settings.Tests -c Release
dotnet run --project tests/Glasscape.Wallpaper.Tests -c Release
dotnet run --project tests/Glasscape.UI.Tests -c Release
dotnet run --project tests/Glasscape.Packaging.Tests -c Release
```

기본 UI 검사는 창을 띄우지 않고 번역 리소스·언어 선택·설정 저장을 확인합니다. `--store-capture`는 사용자 설정을 읽지 않고 숨겨진 창을 이미지로 렌더링합니다. 실제 창 표시·모니터 왕복·최소 크기를 검사하려면 사용자가 직접 `--interactive-layout --capture`를 지정하세요. 이 옵션은 여러 테스트 창을 표시합니다.

MSIXBUNDLE 생성과 검증은 [packaging/README.md](packaging/README.md)에 설명했습니다.

네이티브 의존성을 복원하거나 배포본을 만들 때:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/fetch-native.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/fetch-mpv.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1
```

다운로드 해시를 검사한 공식 바이너리를 사용합니다. 샘플 생성용 FFmpeg는 실행 배포에 포함하지 않습니다. 샘플 재생은 mpv만 사용합니다.

`tests/Glasscape.Integration/bin/Release/net10.0-windows10.0.19041.0/Glasscape.Integration.exe`는 실제 작업표시줄 투명화/복원을 검사합니다. `tests/Glasscape.Desktop.Integration/bin/Release/net10.0-windows10.0.19041.0/Glasscape.Desktop.Integration.exe`는 실제 자동 숨김/시작 항목 등록·해제와 동영상 프레임, 일시정지/재개, 모니터·입력·프로세스 정리를 검사합니다. 직접 실행할 경우 두 EXE를 파일 탐색기에서 일반 권한으로 실행하세요. 검증 중 잠시 창을 최소화하고 끝나면 복원하며, 시작 항목과 자동 숨김도 원래 상태로 복구합니다. 결과는 `artifacts/integration`에 저장됩니다. 개발 명령을 관리자 권한 터미널에서 실행했다면 테스트 EXE 자체는 파일 탐색기로 실행하세요.

## 호환성과 출처

실제 개발/통합 검증 환경은 Windows 11 빌드 26200 x64입니다. Windows 10의 일반 작업표시줄과 이전 WorkerW 경로도 구현되어 있지만 이 컴퓨터에서는 직접 실행하지 않았습니다. Windows 내부 바탕화면 구조를 사용하므로 모든 향후 Windows 버전과 그래픽 드라이버에서의 동작을 보장하지는 않습니다.

작업표시줄은 [TranslucentTB 2026.2](https://github.com/TranslucentTB/TranslucentTB/releases/tag/2026.2)의 공식 `ExplorerTAP.dll`을 호출합니다. 바탕화면 연결은 사용자가 제공한 [Lively 저장소](https://github.com/Lively-wallpaper/lively)와 현재 [Lively 구현](https://github.com/lively-community/lively)을 참고했고, 동영상은 [mpv 공식 0.41.0 배포 파일](https://github.com/mpv-player/mpv/releases/tag/v0.41.0)을 별도 프로세스로 사용합니다. 버전·해시·빌드와 소스 출처는 `third_party/mpv/NOTICE.md`에 실제 확인 범위와 함께 기록했습니다.

라이선스는 범위별로 구분합니다. BOMTOBE가 독립적으로 작성한 앱 코드·테스트·빌드 스크립트·문서는 [MIT](LICENSE-MIT.txt)로 제공합니다(Copyright © 2026 BOMTOBE). TranslucentTB와 Lively를 참고·변형한 작업표시줄/바탕화면 통합 파일 네 개는 [GPL 버전 3](LICENSE-GPL-3.0.md)을 유지하며, 정확한 파일 목록과 출처는 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)에 있습니다. 이 코드와 ExplorerTAP을 함께 사용하는 배포 앱 전체에는 GPL 배포 조건이 적용되며, MIT만으로 배포되는 실행 파일은 아닙니다.

배포본에 C# 소스 ZIP, TranslucentTB 소스/라이선스, mpv와 의존성 출처/라이선스를 함께 제공합니다. 기존 제3자 라이선스는 MIT로 바꾸지 않습니다. mpv 원본 배포 파일의 일부 의존성 빌드 커밋은 공개된 메타데이터로 완전히 식별되지 않으며 이 한계도 NOTICE에 명시합니다. 자체 제작한 오로라·물결 영상과 포스터는 별도의 CC0-1.0 라이선스입니다. 문의: [bomtobe.sw@gmail.com](mailto:bomtobe.sw@gmail.com).

`0x800401E3`은 관리자 권한 앱과 일반 권한 Explorer 사이에서 발생할 수 있습니다. 파일 탐색기에서 일반 실행하세요. `0x80070666`은 Explorer에 다른 버전의 모듈이 남아 있다는 뜻이므로 Windows를 다시 시작한 뒤 실행하세요.
