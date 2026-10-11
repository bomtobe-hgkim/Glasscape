# Glasscape 기본 동영상 샘플

기본 라이브러리와 배포본에는 자체 제작한 절차적 애니메이션 `aurora-flow`, `ocean-drift` 두 개만 포함합니다. 아래 CC0 표기는 두 영상의 MP4·포스터와 생성 스크립트에 적용됩니다. 전체 문구는 [LICENSE-CC0.txt](LICENSE-CC0.txt)에 보존했습니다.

| 파일 | 장면 | 형식 | 크기 |
| --- | --- | --- | --- |
| [aurora-flow.mp4](aurora-flow.mp4) | 네이비 바탕 위로 천천히 흐르는 청록빛 오로라 | H.264 / MP4, 1280×720, 30fps, 8초, 무음 | 1,484,615 bytes |
| [ocean-drift.mp4](ocean-drift.mp4) | 깊은 바닷빛과 부드러운 물결의 움직임 | H.264 / MP4, 1280×720, 30fps, 8초, 무음 | 1,468,861 bytes |

포스터는 각 동영상의 첫 프레임을 디코딩해 만든 `aurora-flow.png`, `ocean-drift.png`입니다. 배경화면 앱의 동영상 가져오기에서 위 MP4를 선택하거나 HTML 미리보기의 가져오기 영역으로 끌어놓으세요.

rain, rain2, town의 원본 MP4와 포스터는 개발 폴더에 보존하지만 기본 라이브러리, 실행 배포본, 포터블 ZIP, 소스 ZIP 및 Store 패키지에 포함하지 않습니다. 기존 사용자가 가져온 영상과 선택한 배경화면은 변경하지 않습니다. 앱은 기본적으로 모든 배경화면의 소리를 끕니다.

## 원본 생성과 검증

`generate_samples.py`의 수학식으로 RGB 빛의 분포를 만들고 FFmpeg `geq` 필터로 렌더링했습니다. 시간에 관한 모든 항은 정확히 8초 주기로 반복되는 사인 함수입니다. 프레임을 640×360에서 계산하고 Lanczos로 1280×720에 확대합니다. 무음 H.264/yuv420p, 약 1.5Mbit/s CBR, faststart MP4로 인코딩했습니다. 원본과 코드는 이 프로젝트에서 Codex의 도움으로 작성했습니다. 저작권 또는 관련 권리가 존재하는 범위에서 CC0-1.0에 따라 제공됩니다.

기본 생성 도구는 `../../third_party/mpv/generation-tools/ffmpeg.exe`입니다. FFmpeg 프로젝트가 Windows 다운로드 페이지에서 안내하는 Gyan 빌드의 8.1.2 버전을 사용했습니다. 운영체제에 설치하지 않았으며 이 도구는 동영상 샘플을 다시 만드는 개발 도구입니다. 앱 실행에는 포함하지 않아도 됩니다.

```powershell
python assets/samples/generate_samples.py
# 다른 FFmpeg가 있다면:
python assets/samples/generate_samples.py --ffmpeg C:/path/to/ffmpeg.exe
```

검증 결과:

- FFprobe로 각 파일의 240프레임, 1280×720, 30/1fps, 8.000초, H.264/yuv420p, 오디오 트랙 없음 확인
- FFmpeg로 두 동영상의 모든 프레임을 오류 없이 디코딩
- 배포할 mpv 0.41.0으로 `--vo=null --ao=null --hwdec=no`를 사용해 화면과 소리를 출력하지 않고 재생, 정상 EOF 종료 확인
- 마지막 → 첫 프레임 밝기 변화가 인접 프레임 변화와 비슷함: 오로라 1.033배, 물결 0.941배. 결과는 `verification.json`에 기록
- 수학적 원본의 시간 0초 / 8초 끝점 오차는 8×10⁻¹⁴ 이하. 프레임은 0초부터 7.966…초까지 담아 같은 첫 프레임을 중복하지 않습니다.

`manifest.json`에는 실제 파일 크기, SHA-256, 인코더 버전과 메타데이터가 있습니다. H.264의 손실 압축 때문에 인코딩된 끝점 픽셀이 비트 단위로 같다는 의미는 아닙니다.

## SHA-256

```text
50ef7d5688f265b1627f77107130a8cab01bc48126fe38422ca4e0e08f85eec8  aurora-flow.mp4
75aee63ad578ff60bb399b7d963d6dd81d60129a187fd9ca2c96f46e1990e482  ocean-drift.mp4
4b056fce1017e39c87b44334ad47e5dd18581b613b1d81ee6d4817b761462050  aurora-flow.png
b4139c7165b9f123af4bebe5f7b1b6a58e655bee6b51205b2a08f6c29c9b1f5c  ocean-drift.png
```

## 도구 출처

- [FFmpeg 공식 다운로드 안내](https://ffmpeg.org/download.html)
- [Gyan Windows 빌드](https://www.gyan.dev/ffmpeg/builds/)
- 생성에 사용한 패키지: `https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.zip`
- 패키지 SHA-256: `db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec`, 제작자 제공 `.sha256`와 대조
- 인코더 및 패키지의 원래 README/LICENSE는 `third_party/mpv/generation-tools/`에 보존
- [CC0 1.0 공식 문구](https://creativecommons.org/publicdomain/zero/1.0/legalcode.txt)
